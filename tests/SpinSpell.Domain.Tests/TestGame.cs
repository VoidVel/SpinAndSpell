using SpinSpell.Domain;

namespace SpinSpell.Domain.Tests;

/// <summary>
/// Builds states and drives the engine so tests read as game narration rather
/// than as record construction.
/// </summary>
internal sealed class TestGame
{
    public GameState State { get; private set; }
    public List<GameEvent> LastEvents { get; private set; } = [];

    private readonly IRandomSource _random;
    private DateTimeOffset _now = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);

    public TestGame(int seed = 1, RoomSettings? settings = null)
    {
        _random = new SeededRandom(seed);
        State = GameState.NewRoom(Guid.NewGuid(), "ABC123", settings ?? new RoomSettings(), _now);
    }

    public Guid OwnerId { get; private set; }

    /// <summary>
    /// The owner is registered alongside everyone else, because they are a
    /// player like everyone else — they write questions, spin and score.
    /// </summary>
    public TestGame WithOwner(string name = "Owner")
    {
        OwnerId = Guid.NewGuid();
        Players[name] = OwnerId;
        Send(new JoinRoom(OwnerId, name, AsOwner: true));
        return this;
    }

    public Dictionary<string, Guid> Players { get; } = [];

    public TestGame WithPlayers(params string[] names)
    {
        foreach (var name in names)
        {
            var id = Guid.NewGuid();
            Players[name] = id;
            Send(new JoinRoom(id, name, AsOwner: false));
        }
        return this;
    }

    public Guid this[string name] => Players[name];

    /// <summary>Gives every player enough questions to satisfy the minimum.</summary>
    public TestGame WithQuestions(int perPlayer = 4)
    {
        Send(new OpenSubmissions { ActorId = OwnerId });

        foreach (var (name, id) in Players)
        {
            for (var i = 0; i < perPlayer; i++)
            {
                Send(new SubmitQuestion(
                    Guid.NewGuid(),
                    i % 2 == 0 ? QuestionKind.Truth : QuestionKind.Dare,
                    $"{name} question {i}")
                { ActorId = id });
            }
        }
        return this;
    }

    public EngineResult Send(Command command)
    {
        var result = GameEngine.Apply(State, command, new EngineContext(_now, _random));
        State = result.State;
        LastEvents = [.. result.Events];
        return result;
    }

    /// <summary>Advances the clock and fires whatever timer the phase is waiting on.</summary>
    public TestGame Tick()
    {
        var phase = State.Phase;
        _now = State.PhaseDeadline is { } deadline && deadline > _now
            ? deadline.AddMilliseconds(1)
            : _now.AddSeconds(1);
        Send(new PhaseTimeout(phase));
        return this;
    }

    /// <summary>
    /// Plays one complete round: spin settles, challenger picks, challenger
    /// judges. The verdict comes from inside the round — there is nobody
    /// standing outside it to ask.
    /// </summary>
    public TestGame PlayRound(RoundOutcome outcome = RoundOutcome.Success)
    {
        Tick(); // Spinning -> Choosing

        var round = State.CurrentRound!;
        Send(new ChooseQuestion(round.Hand[0]) { ActorId = round.ChallengerId });
        Send(new Judge(outcome) { ActorId = round.ChallengerId });

        Tick(); // Celebration -> Scoreboard
        return this;
    }

    public T Event<T>() where T : GameEvent => LastEvents.OfType<T>().Single();

    public bool Has<T>() where T : GameEvent => LastEvents.OfType<T>().Any();

    public CommandRejected Rejection => LastEvents.OfType<CommandRejected>().Single();

    public Player Player(string name) => State.PlayerById(Players[name])!;
}
