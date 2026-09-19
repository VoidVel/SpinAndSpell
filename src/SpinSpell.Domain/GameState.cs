namespace SpinSpell.Domain;

/// <summary>The phase machine from the development plan (§6).</summary>
public enum Phase
{
    Lobby,
    Submission,
    Spinning,
    Choosing,
    Revealed,
    Judging,
    Celebration,
    Scoreboard,
    Ended
}

public enum QuestionKind { Truth, Dare }

/// <summary>
/// What the owner lets the room write. Set in the lobby and locked once
/// submissions open — see GameEngine.OnSetQuestionMode for why.
/// </summary>
public enum QuestionMode { Both, TruthsOnly, DaresOnly }

public enum RoundOutcome { Success, Skipped }

/// <param name="IsOwner">
/// Whoever created the room. It buys them the pacing controls — the mode, the
/// start, the next round, the end — and nothing else. They write questions,
/// spin, challenge, perform and score exactly like everyone else, because a
/// person sitting at the table not playing is a person the game wasted.
/// </param>
public sealed record Player(
    Guid Id,
    string Name,
    int ColourSlot,
    bool IsOwner,
    int Score,
    int TurnsAsTarget,
    int TurnsAsChallenger,
    bool Connected,
    DateTimeOffset JoinedAt)
{
    public static Player Create(Guid id, string name, int colourSlot, bool isOwner, DateTimeOffset now) =>
        new(id, name, colourSlot, isOwner, Score: 0, TurnsAsTarget: 0, TurnsAsChallenger: 0, Connected: true, JoinedAt: now);
}

public sealed record Question(
    Guid Id,
    Guid AuthorId,
    QuestionKind Kind,
    string Text,
    bool Used);

/// <summary>The round currently in flight. Null outside the round loop.</summary>
public sealed record Round(
    int Index,
    Guid ChallengerId,
    Guid TargetId,
    IReadOnlyList<Guid> Hand,
    Guid? ChosenQuestionId,
    RoundOutcome? Outcome);

/// <summary>Room settings. Defaults are the §11 decisions from the plan.</summary>
/// <param name="ChoosingTimeoutSeconds">
/// How long the challenger gets before a card is picked for them. Always on —
/// unlike the reveal timer this is not a game mechanic, it is the thing that
/// stops one person's locked phone from ending the party.
/// </param>
/// <param name="Mode">
/// Whether the room writes truths, dares, or both. A group that wants a purely
/// filthy night of dares, or a mellow one of truths, should not have to police
/// it themselves — the owner picks once and the app enforces it.
/// </param>
public sealed record RoomSettings(
    int MinQuestionsPerPlayer = 4,
    bool TimerEnabled = false,
    int RevealTimerSeconds = 45,
    int HandSize = 3,
    int PointsForSuccess = 1,
    int ChoosingTimeoutSeconds = 45,
    QuestionMode Mode = QuestionMode.Both);

/// <summary>
/// The entire game. Immutable — every engine transition returns a new instance.
/// This is what gets serialised into the rooms.state jsonb column verbatim.
///
/// Everyone in Players plays. There is no spectator seat and no separate host
/// screen: the room is exactly the people holding phones.
/// </summary>
public sealed record GameState(
    Guid RoomId,
    string Code,
    Phase Phase,
    RoomSettings Settings,
    IReadOnlyList<Player> Players,
    IReadOnlyList<Question> Questions,
    Round? CurrentRound,
    int RoundsPlayed,
    Guid? PreviousChallengerId,
    Guid? PreviousTargetId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FirstChallengeAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset? PhaseDeadline,
    int Skips)
{
    public static GameState NewRoom(Guid roomId, string code, RoomSettings settings, DateTimeOffset now) =>
        new(roomId, code, Phase.Lobby, settings,
            Players: [], Questions: [], CurrentRound: null, RoundsPlayed: 0,
            PreviousChallengerId: null, PreviousTargetId: null,
            CreatedAt: now, StartedAt: null, FirstChallengeAt: null, EndedAt: null,
            PhaseDeadline: null, Skips: 0);

    public Player? Owner => Players.FirstOrDefault(p => p.IsOwner);

    public Player? PlayerById(Guid id) => Players.FirstOrDefault(p => p.Id == id);

    public Question? QuestionById(Guid id) => Questions.FirstOrDefault(q => q.Id == id);

    /// <summary>Everyone still holding a live connection — the rotation pool.</summary>
    public IEnumerable<Player> ConnectedPlayers => Players.Where(p => p.Connected);

    public int AvailableQuestionCount => Questions.Count(q => !q.Used);

    /// <summary>Whether the room's mode permits writing this kind of question.</summary>
    public bool Allows(QuestionKind kind) => Settings.Mode switch
    {
        QuestionMode.TruthsOnly => kind == QuestionKind.Truth,
        QuestionMode.DaresOnly => kind == QuestionKind.Dare,
        _ => true
    };

    public int QuestionsBy(Guid playerId) => Questions.Count(q => q.AuthorId == playerId);

    /// <summary>Players who have not yet met the submission minimum.</summary>
    public IEnumerable<Player> PlayersBelowMinimum =>
        Players.Where(p => QuestionsBy(p.Id) < Settings.MinQuestionsPerPlayer);

    public GameState WithPlayer(Player updated) =>
        this with { Players = [.. Players.Select(p => p.Id == updated.Id ? updated : p)] };
}
