using System.Threading.Channels;
using SpinSpell.Domain;
using SpinSpell.Server.Persistence;

namespace SpinSpell.Server.Rooms;

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}

/// <summary>Where engine events go once the actor has applied them.</summary>
public interface IRoomBroadcaster
{
    Task PublishAsync(GameState state, IReadOnlyList<GameEvent> events, CancellationToken ct);
}

/// <summary>
/// One room, one actor, one consumer. Every command for this room — player taps,
/// host taps, and phase timeouts alike — arrives through this single mailbox and
/// is applied in order, so no engine state is ever touched concurrently and there
/// is not a lock anywhere in the game logic.
///
/// Timers are not separate objects: the loop simply waits on the mailbox with a
/// deadline, and treats the deadline elapsing as another command. That is why a
/// late-firing timer cannot race a player's tap.
/// </summary>
public sealed class RoomActor : IAsyncDisposable
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A command plus, optionally, somewhere to put the result. Fire-and-forget
    /// for gameplay taps; awaited for joins, where the caller needs to know
    /// whether the name was taken before it can be issued a token.
    /// </summary>
    private readonly record struct Envelope(Command Command, TaskCompletionSource<EngineResult>? Reply);

    private readonly Channel<Envelope> _mailbox = Channel.CreateBounded<Envelope>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });

    private readonly IRoomStore _store;
    private readonly IRoomBroadcaster _broadcaster;
    private readonly IClock _clock;
    private readonly IRandomSource _random;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _loop;

    private GameState _state;
    private Phase _lastPersistedPhase;

    public RoomActor(
        GameState initial,
        IRoomStore store,
        IRoomBroadcaster broadcaster,
        IClock clock,
        ILogger logger)
    {
        _state = initial;
        _lastPersistedPhase = initial.Phase;
        _store = store;
        _broadcaster = broadcaster;
        _clock = clock;
        _logger = logger;
        _random = new SeededRandom(initial.RoomId.GetHashCode());
        _loop = Task.Run(RunAsync);
    }

    public string Code => _state.Code;

    public Guid RoomId => _state.RoomId;

    /// <summary>
    /// A snapshot for read-only use (projections on connect). Safe because
    /// GameState is immutable — the reference may go stale but never tears.
    /// </summary>
    public GameState Snapshot => _state;

    public event Action<RoomActor>? Evicted;

    /// <summary>Fire-and-forget. False means the mailbox is saturated.</summary>
    public bool Tell(Command command) => _mailbox.Writer.TryWrite(new Envelope(command, null));

    /// <summary>Enqueues and waits for the engine's verdict on this one command.</summary>
    public async Task<EngineResult> AskAsync(Command command, CancellationToken ct = default)
    {
        var reply = new TaskCompletionSource<EngineResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_mailbox.Writer.TryWrite(new Envelope(command, reply)))
        {
            throw new InvalidOperationException("This room is too busy right now.");
        }

        using var registration = ct.Register(() => reply.TrySetCanceled(ct));
        return await reply.Task;
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var envelope = await ReceiveAsync();

                if (envelope is not { } received)
                {
                    _logger.LogInformation("Room {Code} evicted after {Minutes} idle minutes.",
                        _state.Code, IdleTimeout.TotalMinutes);
                    break;
                }

                await HandleAsync(received.Command, received.Reply);

                if (_state.Phase == Phase.Ended)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Room {Code} loop faulted.", _state.Code);
        }
        finally
        {
            await PersistAsync(force: true);
            Evicted?.Invoke(this);
        }
    }

    /// <summary>
    /// Waits for the next command, the current phase deadline, or the idle
    /// timeout — whichever lands first. Returns null when the room should evict.
    /// </summary>
    private async Task<Envelope?> ReceiveAsync()
    {
        var now = _clock.Now;
        var phase = _state.Phase;
        var phaseDeadline = _state.PhaseDeadline;

        if (phaseDeadline is { } due && due <= now)
        {
            return new Envelope(new PhaseTimeout(phase), null);
        }

        // Decide *why* we are waiting before waiting, and hold onto it. Deriving
        // it afterwards from a fresh clock read is a trap: CancelAfter can fire a
        // hair early, the deadline then reads as "not yet due", and the room gets
        // evicted mid-game instead of advancing a phase. Nothing rehydrates it,
        // because every client is sitting there waiting to be told what happens
        // next — so the party just stops. Do not reintroduce that.
        var waitingOnPhase = phaseDeadline is { } d && d - now <= IdleTimeout;
        var wait = waitingOnPhase ? phaseDeadline!.Value - now : IdleTimeout;

        using var timer = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timer.CancelAfter(wait);

        try
        {
            return await _mailbox.Reader.ReadAsync(timer.Token);
        }
        catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
        {
            return waitingOnPhase
                ? new Envelope(new PhaseTimeout(phase), null)
                : null; // Genuinely idle for the full timeout — let the room go.
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    private async Task HandleAsync(Command command, TaskCompletionSource<EngineResult>? reply)
    {
        EngineResult result;

        try
        {
            result = GameEngine.Apply(_state, command, new EngineContext(_clock.Now, _random));
        }
        catch (Exception ex)
        {
            // The engine is meant to reject rather than throw. If it ever does
            // throw, one bad command must not take the whole party down with it.
            _logger.LogError(ex, "Engine faulted on {Command} in room {Code}.", command.GetType().Name, _state.Code);
            reply?.TrySetException(ex);
            return;
        }

        var previousPhase = _state.Phase;
        _state = result.State;
        reply?.TrySetResult(result);

        if (result.Events.Count > 0)
        {
            await _broadcaster.PublishAsync(_state, result.Events, _shutdown.Token);
        }

        // Persist on phase change, not per event — roughly ten writes a game.
        if (_state.Phase != previousPhase)
        {
            await PersistAsync();
        }
    }

    private async Task PersistAsync(bool force = false)
    {
        if (!force && _state.Phase == _lastPersistedPhase)
        {
            return;
        }

        try
        {
            await _store.SaveAsync(_state, CancellationToken.None);
            _lastPersistedPhase = _state.Phase;
        }
        catch (Exception ex)
        {
            // A failed write must not take the room down mid-party. The live
            // game is in memory; persistence is for recovery, not for play.
            _logger.LogError(ex, "Failed to persist room {Code}.", _state.Code);
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();
        _mailbox.Writer.TryComplete();

        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        _shutdown.Dispose();
    }
}
