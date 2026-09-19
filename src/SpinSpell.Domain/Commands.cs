namespace SpinSpell.Domain;

/// <summary>
/// Everything that can happen to a room. The engine is the only thing that
/// interprets these, and it does so one at a time per room (§2.2).
/// </summary>
public abstract record Command
{
    /// <summary>Who sent it. Null for server-originated commands (timeouts).</summary>
    public Guid? ActorId { get; init; }
}

public sealed record JoinRoom(Guid PlayerId, string Name, bool AsOwner) : Command;

public sealed record LeaveRoom(Guid PlayerId) : Command;

public sealed record SetConnected(Guid PlayerId, bool Connected) : Command;

public sealed record SubmitQuestion(Guid QuestionId, QuestionKind Kind, string Text) : Command;

public sealed record DeleteQuestion(Guid QuestionId) : Command;

/// <summary>
/// Reword your own question before it is played. Applies to every question
/// equally — a prompt picked from the library becomes an ordinary authored
/// question the moment it lands, with nothing marking where it came from.
/// </summary>
public sealed record EditQuestion(Guid QuestionId, string Text) : Command;

/// <summary>Owner, in the lobby only. Truths, dares, or both.</summary>
public sealed record SetQuestionMode(QuestionMode Mode) : Command;

public sealed record OpenSubmissions : Command;

/// <summary>Owner, from Submission. Enters the first spin.</summary>
public sealed record StartGame : Command;

/// <summary>Server-originated: the spin animation window has elapsed.</summary>
public sealed record SpinSettled : Command;

public sealed record ChooseQuestion(Guid QuestionId) : Command;

/// <summary>The challenger's verdict on the round they set up.</summary>
public sealed record Judge(RoundOutcome Outcome) : Command;

/// <summary>Server-originated: the celebration animation window has elapsed.</summary>
public sealed record CelebrationFinished : Command;

public sealed record NextRound : Command;

public sealed record TransferOwner(Guid NewOwnerId) : Command;

public sealed record EndGame : Command;

/// <summary>Server-originated: a phase deadline passed (§6).</summary>
public sealed record PhaseTimeout(Phase ExpectedPhase) : Command;
