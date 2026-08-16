using System.Collections.Concurrent;
using System.Security.Cryptography;
using SpinSpell.Domain;
using SpinSpell.Server.Persistence;

namespace SpinSpell.Server.Rooms;

/// <summary>
/// Creates a short-lived DbContext per call instead of pinning one open for the
/// life of a room — an actor can sit idle for half an hour between writes.
/// </summary>
public sealed class ScopedRoomStore(IServiceScopeFactory scopes) : IRoomStore
{
    private async Task<T> WithStore<T>(Func<IRoomStore, Task<T>> work)
    {
        using var scope = scopes.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<RoomStore>());
    }

    public Task<GameState?> LoadAsync(string code, CancellationToken ct = default) =>
        WithStore(s => s.LoadAsync(code, ct));

    public Task<GameState?> LoadAsync(Guid roomId, CancellationToken ct = default) =>
        WithStore(s => s.LoadAsync(roomId, ct));

    public Task<bool> CodeIsFreeAsync(string code, CancellationToken ct = default) =>
        WithStore(s => s.CodeIsFreeAsync(code, ct));

    public Task SaveAsync(GameState state, CancellationToken ct = default) =>
        WithStore(async s => { await s.SaveAsync(state, ct); return true; });

    public Task PurgeExpiredAsync(TimeSpan retention, CancellationToken ct = default) =>
        WithStore(async s => { await s.PurgeExpiredAsync(retention, ct); return true; });
}

/// <summary>
/// Room code → live actor, with on-demand rehydration from Postgres for rooms
/// that were evicted or that outlived a restart.
/// </summary>
public sealed class RoomRegistry(
    IRoomStore store,
    IRoomBroadcaster broadcaster,
    IClock clock,
    ILogger<RoomRegistry> logger,
    ILoggerFactory loggerFactory)
{
    /// <summary>No 0/O/1/I/5/S — these get misread across a room, and misheard.</summary>
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRTUVWXYZ2346789";
    private const int CodeLength = 4;

    private readonly ConcurrentDictionary<string, RoomActor> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int LiveRoomCount => _rooms.Count;

    public async Task<RoomActor> CreateAsync(RoomSettings settings, CancellationToken ct = default)
    {
        var code = await AllocateCodeAsync(ct);
        var state = GameState.NewRoom(Guid.CreateVersion7(), code, settings, clock.Now);

        await store.SaveAsync(state, ct);

        await _gate.WaitAsync(ct);
        try
        {
            var actor = Attach(state);
            logger.LogInformation("Room {Code} created.", code);
            return actor;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RoomActor?> FindAsync(string code, CancellationToken ct = default)
    {
        code = code.Trim().ToUpperInvariant();

        if (_rooms.TryGetValue(code, out var live))
        {
            return live;
        }

        await _gate.WaitAsync(ct);
        try
        {
            // Re-check: another connection may have rehydrated it while we waited.
            if (_rooms.TryGetValue(code, out live))
            {
                return live;
            }

            var state = await store.LoadAsync(code, ct);

            if (state is null || state.Phase == Phase.Ended)
            {
                return null;
            }

            logger.LogInformation("Room {Code} rehydrated from storage.", code);
            return Attach(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    private RoomActor Attach(GameState state)
    {
        var actor = new RoomActor(state, store, broadcaster, clock, loggerFactory.CreateLogger<RoomActor>());
        actor.Evicted += Detach;
        _rooms[state.Code] = actor;
        return actor;
    }

    private void Detach(RoomActor actor)
    {
        _rooms.TryRemove(new KeyValuePair<string, RoomActor>(actor.Code, actor));
    }

    private async Task<string> AllocateCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var code = GenerateCode();

            if (!_rooms.ContainsKey(code) && await store.CodeIsFreeAsync(code, ct))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not allocate a free room code.");
    }

    private static string GenerateCode() =>
        RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);

    public async Task ShutdownAsync()
    {
        foreach (var actor in _rooms.Values.ToList())
        {
            await actor.DisposeAsync();
        }
    }
}
