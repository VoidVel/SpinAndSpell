using System.ComponentModel.DataAnnotations;

namespace SpinSpell.Server.Persistence;

/// <summary>
/// The room is the aggregate. Nothing queries across rooms during gameplay, so
/// nothing is normalised — players, questions and rounds all live inside State.
/// </summary>
public sealed class RoomRecord
{
    public Guid Id { get; set; }

    [MaxLength(6)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(16)]
    public string Status { get; set; } = RoomStatus.Active;

    /// <summary>The serialised GameState, stored as jsonb.</summary>
    public string State { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class RoomStatus
{
    public const string Active = "active";
    public const string Ended = "ended";
}

/// <summary>
/// Written once, when a game ends. This is the entire analytics story — every
/// success signal in §11 of the source of truth is a query over this table.
/// Deliberately anonymous: counts only, no player names and no question text.
/// </summary>
public sealed class GameResultRecord
{
    public Guid RoomId { get; set; }

    public int PlayerCount { get; set; }

    public int RoundsPlayed { get; set; }

    public int Skips { get; set; }

    public int QuestionsSubmitted { get; set; }

    /// <summary>Null if the game ended before anyone was ever challenged.</summary>
    public int? SecondsToFirstChallenge { get; set; }

    public int DurationSeconds { get; set; }

    public DateTimeOffset EndedAt { get; set; }
}
