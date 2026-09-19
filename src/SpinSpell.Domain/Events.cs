namespace SpinSpell.Domain;

/// <summary>
/// What the engine emits. The server translates these into SignalR pushes (§5).
/// Each event carries its own audience so the transport layer never has to
/// re-derive who may see it — leaking the challenger's hand is a correctness bug.
/// </summary>
public abstract record GameEvent
{
    public virtual Audience Audience => Audience.Room;
}

public enum AudienceKind { Room, Player }

public readonly record struct Audience(AudienceKind Kind, Guid? PlayerId = null)
{
    public static readonly Audience Room = new(AudienceKind.Room);
    public static Audience To(Guid playerId) => new(AudienceKind.Player, playerId);
}

public sealed record PlayerJoined(Player Player) : GameEvent;

public sealed record PlayerLeft(Guid PlayerId) : GameEvent;

public sealed record PlayerConnectionChanged(Guid PlayerId, bool Connected) : GameEvent;

public sealed record OwnerChanged(Guid NewOwnerId) : GameEvent;

public sealed record QuestionModeChanged(QuestionMode Mode) : GameEvent;

public sealed record SubmissionProgress(IReadOnlyDictionary<Guid, int> CountsByPlayer, int Total) : GameEvent;

public sealed record PhaseChanged(Phase Phase, DateTimeOffset? Deadline) : GameEvent;

public sealed record SpinResolved(Guid ChallengerId, Guid TargetId, int AnimationMs) : GameEvent;

/// <summary>Private to the challenger. Never broadcast — see plan §5.</summary>
public sealed record HandDealt(Guid ChallengerId, IReadOnlyList<Question> Hand) : GameEvent
{
    public override Audience Audience => Audience.To(ChallengerId);
}

public sealed record QuestionRevealed(QuestionKind Kind, string Text, DateTimeOffset? Deadline) : GameEvent;

public sealed record RoundJudged(RoundOutcome Outcome, Guid TargetId, int Points) : GameEvent;

public sealed record ScoresUpdated(IReadOnlyDictionary<Guid, int> Scores) : GameEvent;

public sealed record QuestionPoolLow(int Remaining) : GameEvent;

public sealed record GameEnded(IReadOnlyList<Player> FinalStandings) : GameEvent;

/// <summary>A command the engine refused. Goes back to whoever sent it.</summary>
public sealed record CommandRejected(Guid? PlayerId, string Code, string Message) : GameEvent
{
    public override Audience Audience => PlayerId is { } id ? Audience.To(id) : Audience.Room;
}
