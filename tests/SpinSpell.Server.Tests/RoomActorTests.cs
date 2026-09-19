using Microsoft.Extensions.Logging.Abstractions;
using SpinSpell.Domain;
using SpinSpell.Server.Persistence;
using SpinSpell.Server.Rooms;

namespace SpinSpell.Server.Tests;

internal sealed class RecordingBroadcaster : IRoomBroadcaster
{
    private readonly Lock _gate = new();
    private readonly List<GameEvent> _events = [];

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public IReadOnlyList<GameEvent> Events
    {
        get
        {
            lock (_gate) return [.. _events];
        }
    }

    public async Task PublishAsync(GameState state, IReadOnlyList<GameEvent> events, CancellationToken ct)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        lock (_gate) _events.AddRange(events);
    }

    public int CountOf<T>() => Events.OfType<T>().Count();

    public async Task<bool> WaitFor<T>(int count = 1, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (CountOf<T>() >= count) return true;
            await Task.Delay(20);
        }
        return false;
    }
}

internal sealed class InMemoryStore : IRoomStore
{
    private readonly Dictionary<Guid, GameState> _saved = [];

    public int Saves { get; private set; }

    public Task<GameState?> LoadAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(_saved.Values.FirstOrDefault(s => s.Code == code));

    public Task<GameState?> LoadAsync(Guid roomId, CancellationToken ct = default) =>
        Task.FromResult(_saved.TryGetValue(roomId, out var s) ? s : null);

    public Task SaveAsync(GameState state, CancellationToken ct = default)
    {
        _saved[state.RoomId] = state;
        Saves++;
        return Task.CompletedTask;
    }

    public Task<bool> CodeIsFreeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(_saved.Values.All(s => s.Code != code));

    public Task PurgeExpiredAsync(TimeSpan retention, CancellationToken ct = default) => Task.CompletedTask;
}

public class RoomActorTests
{
    private static (RoomActor actor, RecordingBroadcaster bus, InMemoryStore store, Guid owner, Guid[] players)
        StartedGame(RoomSettings? settings = null)
    {
        var bus = new RecordingBroadcaster();
        var store = new InMemoryStore();

        var state = GameState.NewRoom(Guid.NewGuid(), "TEST", settings ?? new RoomSettings(), DateTimeOffset.UtcNow);
        var actor = new RoomActor(state, store, bus, new SystemClock(), NullLogger.Instance);

        // The owner is the first entry in players, because they play too and
        // therefore owe the pot the same share of questions as everyone else.
        var owner = Guid.NewGuid();
        var players = new[] { owner, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        actor.Tell(new JoinRoom(owner, "Owner", AsOwner: true));
        foreach (var (id, i) in players.Skip(1).Select((id, i) => (id, i)))
        {
            actor.Tell(new JoinRoom(id, $"P{i}", AsOwner: false));
        }

        actor.Tell(new OpenSubmissions { ActorId = owner });

        foreach (var id in players)
        {
            for (var i = 0; i < 6; i++)
            {
                actor.Tell(new SubmitQuestion(Guid.NewGuid(),
                    i % 2 == 0 ? QuestionKind.Truth : QuestionKind.Dare, $"q{i}") { ActorId = id });
            }
        }

        return (actor, bus, store, owner, players);
    }

    /// <summary>
    /// The regression that matters most.
    ///
    /// The actor waits on its mailbox with a deadline. CancelAfter can fire a
    /// hair before that deadline; an earlier version then re-read the clock,
    /// concluded the phase was not due, and took the idle-eviction branch —
    /// silently killing a live room. No client notices, because they are all
    /// waiting to be told what happens next. The party simply stops.
    ///
    /// So: play several rounds and insist every spin settles on its own.
    /// </summary>
    [Fact]
    public async Task Phase_timers_keep_firing_across_many_rounds_and_never_evict_a_live_room()
    {
        var (actor, bus, _, owner, _) = StartedGame();

        Assert.True(await bus.WaitFor<SubmissionProgress>(24), "questions never landed");

        actor.Tell(new StartGame { ActorId = owner });

        const int rounds = 6;

        for (var round = 1; round <= rounds; round++)
        {
            // Spinning -> Choosing happens purely on the server timer.
            Assert.True(await bus.WaitFor<HandDealt>(round),
                $"round {round}: the spin never settled — the room was evicted mid-game.");

            var current = actor.Snapshot.CurrentRound!;
            actor.Tell(new ChooseQuestion(current.Hand[0]) { ActorId = current.ChallengerId });

            Assert.True(await bus.WaitFor<QuestionRevealed>(round), $"round {round}: no reveal");

            actor.Tell(new Judge(RoundOutcome.Success) { ActorId = current.ChallengerId });

            // Celebration -> Scoreboard is also timer-driven.
            Assert.True(await bus.WaitFor<RoundJudged>(round), $"round {round}: no judgment");
            await WaitForPhase(actor, Phase.Scoreboard);

            if (round < rounds) actor.Tell(new NextRound { ActorId = owner });
        }

        Assert.Equal(rounds, actor.Snapshot.RoundsPlayed);
        await actor.DisposeAsync();
    }

    [Fact]
    public async Task A_stalled_challenger_does_not_freeze_the_room()
    {
        var (actor, bus, _, owner, _) = StartedGame(
            new RoomSettings(MinQuestionsPerPlayer: 4, ChoosingTimeoutSeconds: 2));

        Assert.True(await bus.WaitFor<SubmissionProgress>(24));
        actor.Tell(new StartGame { ActorId = owner });

        // Nobody ever chooses. The engine should pick for them and move on.
        Assert.True(await bus.WaitFor<QuestionRevealed>(1, timeoutMs: 15_000),
            "the room stalled waiting on a challenger who never picked");

        await actor.DisposeAsync();
    }

    [Fact]
    public async Task Commands_are_applied_in_order_under_a_burst()
    {
        var bus = new RecordingBroadcaster { Delay = TimeSpan.FromMilliseconds(2) };
        var store = new InMemoryStore();
        var state = GameState.NewRoom(Guid.NewGuid(), "BURST", new RoomSettings(), DateTimeOffset.UtcNow);
        var actor = new RoomActor(state, store, bus, new SystemClock(), NullLogger.Instance);

        var owner = Guid.NewGuid();
        actor.Tell(new JoinRoom(owner, "Owner", AsOwner: true));

        var player = Guid.NewGuid();
        actor.Tell(new JoinRoom(player, "P0", AsOwner: false));
        actor.Tell(new JoinRoom(Guid.NewGuid(), "P1", AsOwner: false));
        actor.Tell(new OpenSubmissions { ActorId = owner });

        // Fire from several threads at once; the mailbox must serialise them.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 5; i++)
            {
                actor.Tell(new SubmitQuestion(Guid.NewGuid(), QuestionKind.Truth, $"t{t}-{i}") { ActorId = player });
            }
        })));

        await bus.WaitFor<SubmissionProgress>(40);

        // 30 is the per-player cap; the point is that no write was lost or torn.
        Assert.Equal(30, actor.Snapshot.QuestionsBy(player));
        await actor.DisposeAsync();
    }

    [Fact]
    public async Task State_is_persisted_on_phase_change_not_on_every_event()
    {
        var (actor, bus, store, owner, _) = StartedGame();

        Assert.True(await bus.WaitFor<SubmissionProgress>(24));
        var afterTwentyFourQuestions = store.Saves;

        actor.Tell(new StartGame { ActorId = owner });
        Assert.True(await bus.WaitFor<HandDealt>(1));

        // Two dozen submissions sat inside one phase and cost at most a couple of
        // writes; the spin and the deal are phase changes and do cost writes.
        Assert.True(afterTwentyFourQuestions <= 3,
            $"submissions triggered {afterTwentyFourQuestions} writes — persistence has crept into the hot path.");
        Assert.True(store.Saves > afterTwentyFourQuestions);

        await actor.DisposeAsync();
    }

    private static async Task WaitForPhase(RoomActor actor, Phase phase, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (actor.Snapshot.Phase == phase) return;
            await Task.Delay(20);
        }
        Assert.Fail($"Never reached {phase}; stuck in {actor.Snapshot.Phase}.");
    }
}
