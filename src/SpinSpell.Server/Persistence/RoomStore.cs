using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SpinSpell.Domain;

namespace SpinSpell.Server.Persistence;

public interface IRoomStore
{
    Task<GameState?> LoadAsync(string code, CancellationToken ct = default);
    Task<GameState?> LoadAsync(Guid roomId, CancellationToken ct = default);
    Task SaveAsync(GameState state, CancellationToken ct = default);
    Task<bool> CodeIsFreeAsync(string code, CancellationToken ct = default);
    Task PurgeExpiredAsync(TimeSpan retention, CancellationToken ct = default);
}

public sealed class RoomStore(SpinSpellDbContext db) : IRoomStore
{
    /// <summary>
    /// Enums as strings so the jsonb column is legible in psql — you will read
    /// this by hand when diagnosing a game that went wrong.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<GameState?> LoadAsync(string code, CancellationToken ct = default)
    {
        var record = await db.Rooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Code == code && r.Status == RoomStatus.Active, ct);

        return record is null ? null : Deserialize(record);
    }

    public async Task<GameState?> LoadAsync(Guid roomId, CancellationToken ct = default)
    {
        var record = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct);
        return record is null ? null : Deserialize(record);
    }

    public async Task SaveAsync(GameState state, CancellationToken ct = default)
    {
        var record = await db.Rooms.FirstOrDefaultAsync(r => r.Id == state.RoomId, ct);
        var now = DateTimeOffset.UtcNow;
        var status = state.Phase == Phase.Ended ? RoomStatus.Ended : RoomStatus.Active;

        if (record is null)
        {
            record = new RoomRecord { Id = state.RoomId, Code = state.Code, CreatedAt = now };
            db.Rooms.Add(record);
        }

        record.Status = status;
        record.State = JsonSerializer.Serialize(state, Json);
        record.UpdatedAt = now;

        if (state.Phase == Phase.Ended && !await db.GameResults.AnyAsync(r => r.RoomId == state.RoomId, ct))
        {
            db.GameResults.Add(Summarise(state, now));
        }

        await db.SaveChangesAsync(ct);
    }

    public Task<bool> CodeIsFreeAsync(string code, CancellationToken ct = default) =>
        db.Rooms.AllAsync(r => r.Code != code || r.Status != RoomStatus.Active, ct);

    /// <summary>
    /// Player-written dares are exactly the content not worth accumulating.
    /// game_results survives the purge — it is anonymous counts.
    /// </summary>
    public async Task PurgeExpiredAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - retention;

        await db.Rooms
            .Where(r => r.Status == RoomStatus.Ended && r.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, "{}"), ct);
    }

    private static GameState Deserialize(RoomRecord record) =>
        JsonSerializer.Deserialize<GameState>(record.State, Json)
        ?? throw new InvalidOperationException($"Room {record.Code} has unreadable state.");

    private static GameResultRecord Summarise(GameState state, DateTimeOffset now) => new()
    {
        RoomId = state.RoomId,
        PlayerCount = state.PlayingPlayers.Count(),
        RoundsPlayed = state.RoundsPlayed,
        Skips = state.Skips,
        QuestionsSubmitted = state.Questions.Count,
        SecondsToFirstChallenge = state is { StartedAt: { } started, FirstChallengeAt: { } first }
            ? (int)(first - started).TotalSeconds
            : null,
        DurationSeconds = (int)((state.EndedAt ?? now) - state.CreatedAt).TotalSeconds,
        EndedAt = state.EndedAt ?? now
    };
}
