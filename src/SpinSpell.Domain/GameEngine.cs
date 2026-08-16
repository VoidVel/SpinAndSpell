namespace SpinSpell.Domain;

/// <summary>Time and randomness, injected. The engine reads no ambient state.</summary>
public sealed record EngineContext(DateTimeOffset Now, IRandomSource Random);

public sealed record EngineResult(GameState State, IReadOnlyList<GameEvent> Events)
{
    public static EngineResult Unchanged(GameState state) => new(state, []);
}

public static class Limits
{
    public const int MaxPlayers = 12;
    public const int MaxNameLength = 20;
    public const int MaxQuestionLength = 280;
    public const int MaxQuestionsPerPlayer = 30;
    public const int MinPlayersToStart = 2;

    /// <summary>How long the host screen gets to run the spin animation (§6.3).</summary>
    public const int SpinAnimationMs = 3200;

    /// <summary>Short and punchy — §6.3 explicitly does not want a cutscene.</summary>
    public const int CelebrationMs = 1800;
}

/// <summary>
/// The whole game, as a pure function. No I/O, no clock, no RNG of its own.
///
///     (GameState, Command, EngineContext) -> (GameState', GameEvent[])
///
/// Every rejection is a returned CommandRejected event, never an exception —
/// a malformed client must not be able to fault the room actor.
/// </summary>
public static class GameEngine
{
    public static EngineResult Apply(GameState state, Command command, EngineContext ctx) => command switch
    {
        JoinRoom c => OnJoin(state, c, ctx),
        LeaveRoom c => OnLeave(state, c, ctx),
        SetConnected c => OnSetConnected(state, c),
        SubmitQuestion c => OnSubmitQuestion(state, c),
        DeleteQuestion c => OnDeleteQuestion(state, c),
        EditQuestion c => OnEditQuestion(state, c),
        SetQuestionMode c => OnSetQuestionMode(state, c),
        OpenSubmissions c => OnOpenSubmissions(state, c),
        StartGame c => OnStartGame(state, c, ctx),
        SpinSettled => OnSpinSettled(state, ctx),
        ChooseQuestion c => OnChooseQuestion(state, c, ctx),
        Judge c => OnJudge(state, c, ctx),
        CelebrationFinished => OnCelebrationFinished(state),
        NextRound c => OnNextRound(state, c, ctx),
        TransferHost c => OnTransferHost(state, c),
        EndGame c => OnEndGame(state, c, ctx),
        PhaseTimeout c => OnPhaseTimeout(state, c, ctx),
        _ => EngineResult.Unchanged(state)
    };

    // ---------------------------------------------------------------- lobby

    private static EngineResult OnJoin(GameState state, JoinRoom c, EngineContext ctx)
    {
        if (state.Phase is not (Phase.Lobby or Phase.Submission))
        {
            return Reject(state, c, "game_in_progress", "This game has already started.");
        }

        if (state.PlayerById(c.PlayerId) is not null)
        {
            return EngineResult.Unchanged(state); // idempotent rejoin
        }

        var name = (c.Name ?? string.Empty).Trim();

        if (name.Length is 0 or > Limits.MaxNameLength)
        {
            return Reject(state, c, "invalid_name", $"Pick a name between 1 and {Limits.MaxNameLength} characters.");
        }

        if (state.Players.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Reject(state, c, "name_taken", $"Someone's already called {name}.");
        }

        if (state.Players.Count >= Limits.MaxPlayers)
        {
            return Reject(state, c, "room_full", $"This room is full ({Limits.MaxPlayers} max).");
        }

        if (c.AsHost && state.Host is not null)
        {
            return Reject(state, c, "host_exists", "This room already has a host.");
        }

        var player = Player.Create(c.PlayerId, name, NextColourSlot(state), c.AsHost, ctx.Now);
        var next = state with { Players = [.. state.Players, player] };

        return new EngineResult(next, [new PlayerJoined(player), Progress(next)]);
    }

    /// <summary>Lowest free slot, so colours stay stable and never collide (§6.1).</summary>
    private static int NextColourSlot(GameState state)
    {
        var taken = state.Players.Select(p => p.ColourSlot).ToHashSet();
        var slot = 0;
        while (taken.Contains(slot))
        {
            slot++;
        }
        return slot;
    }

    private static EngineResult OnLeave(GameState state, LeaveRoom c, EngineContext ctx)
    {
        if (state.PlayerById(c.PlayerId) is not { } leaving)
        {
            return EngineResult.Unchanged(state);
        }

        // Mid-game we only mark them gone — ripping a player out of an active
        // round would invalidate the pairing the host screen is displaying.
        if (state.Phase is not (Phase.Lobby or Phase.Submission))
        {
            return OnSetConnected(state, new SetConnected(c.PlayerId, false));
        }

        var next = state with
        {
            Players = [.. state.Players.Where(p => p.Id != c.PlayerId)],
            Questions = [.. state.Questions.Where(q => q.AuthorId != c.PlayerId || q.Used)]
        };

        List<GameEvent> events = [new PlayerLeft(c.PlayerId), Progress(next)];

        // The host walking out of the lobby hands off rather than killing the room (§9.5).
        if (leaving.IsHost && next.Players.Count > 0)
        {
            var heir = next.Players.OrderBy(p => p.JoinedAt).First();
            next = next.WithPlayer(heir with { IsHost = true });
            events.Add(new HostChanged(heir.Id));
        }

        return new EngineResult(next, events);
    }

    private static EngineResult OnSetConnected(GameState state, SetConnected c)
    {
        if (state.PlayerById(c.PlayerId) is not { } player || player.Connected == c.Connected)
        {
            return EngineResult.Unchanged(state);
        }

        var next = state.WithPlayer(player with { Connected = c.Connected });
        return new EngineResult(next, [new PlayerConnectionChanged(c.PlayerId, c.Connected)]);
    }

    private static EngineResult OnTransferHost(GameState state, TransferHost c)
    {
        if (state.PlayerById(c.NewHostId) is not { } heir)
        {
            return Reject(state, c, "unknown_player", "That player isn't in this room.");
        }

        // Server-originated handoff (grace period lapsed) carries no ActorId.
        if (c.ActorId is { } actor && state.PlayerById(actor)?.IsHost != true)
        {
            return Reject(state, c, "not_host", "Only the host can hand off hosting.");
        }

        var next = state;
        if (state.Host is { } current)
        {
            next = next.WithPlayer(current with { IsHost = false });
        }
        next = next.WithPlayer(heir with { IsHost = true });

        return new EngineResult(next, [new HostChanged(c.NewHostId)]);
    }

    // ----------------------------------------------------------- submission

    /// <summary>
    /// Lobby only, and deliberately so. Once people have written their questions
    /// the mode is effectively load-bearing: flipping a room to dares-only after
    /// everyone wrote truths would strand the whole pool and the game would have
    /// nothing to deal. Locking it at OpenSubmissions makes that unreachable
    /// rather than merely unlikely.
    /// </summary>
    private static EngineResult OnSetQuestionMode(GameState state, SetQuestionMode c)
    {
        if (RequireHost(state, c) is { } denied)
        {
            return denied;
        }

        if (state.Phase != Phase.Lobby)
        {
            return Reject(state, c, "mode_locked",
                "Questions are already being written — the mode is locked in for this game.");
        }

        if (state.Settings.Mode == c.Mode)
        {
            return EngineResult.Unchanged(state);
        }

        var next = state with { Settings = state.Settings with { Mode = c.Mode } };
        return new EngineResult(next, [new QuestionModeChanged(c.Mode)]);
    }

    private static EngineResult OnOpenSubmissions(GameState state, OpenSubmissions c)
    {
        if (RequireHost(state, c) is { } denied)
        {
            return denied;
        }

        if (state.Phase != Phase.Lobby)
        {
            return Reject(state, c, "wrong_phase", "Submissions are already open.");
        }

        if (state.ConnectedPlayers.Count() < Limits.MinPlayersToStart)
        {
            return Reject(state, c, "not_enough_players",
                $"You need at least {Limits.MinPlayersToStart} players to get going.");
        }

        var next = state with { Phase = Phase.Submission, PhaseDeadline = null };
        return new EngineResult(next, [new PhaseChanged(Phase.Submission, null), Progress(next)]);
    }

    private static EngineResult OnSubmitQuestion(GameState state, SubmitQuestion c)
    {
        if (state.Phase is not (Phase.Submission or Phase.Scoreboard))
        {
            return Reject(state, c, "wrong_phase", "You can't add questions right now.");
        }

        if (c.ActorId is not { } authorId || state.PlayerById(authorId) is null)
        {
            return Reject(state, c, "unknown_player", "You're not in this room.");
        }

        if (state.QuestionById(c.QuestionId) is not null)
        {
            return EngineResult.Unchanged(state); // idempotent retry
        }

        // The mode is enforced here rather than only in the UI, so the pool can
        // never contain a kind the room said it did not want.
        if (!state.Allows(c.Kind))
        {
            return Reject(state, c, "wrong_kind",
                state.Settings.Mode == QuestionMode.TruthsOnly
                    ? "This room is truths only."
                    : "This room is dares only.");
        }

        var text = (c.Text ?? string.Empty).Trim();

        if (text.Length is 0 or > Limits.MaxQuestionLength)
        {
            return Reject(state, c, "invalid_question",
                $"Keep it between 1 and {Limits.MaxQuestionLength} characters.");
        }

        if (state.QuestionsBy(authorId) >= Limits.MaxQuestionsPerPlayer)
        {
            return Reject(state, c, "too_many_questions",
                $"You've hit the {Limits.MaxQuestionsPerPlayer} question limit. Save some for next time.");
        }

        var question = new Question(c.QuestionId, authorId, c.Kind, text, Used: false);
        var next = state with { Questions = [.. state.Questions, question] };

        return new EngineResult(next, [Progress(next)]);
    }

    private static EngineResult OnDeleteQuestion(GameState state, DeleteQuestion c)
    {
        if (state.QuestionById(c.QuestionId) is not { } question)
        {
            return EngineResult.Unchanged(state);
        }

        if (question.AuthorId != c.ActorId)
        {
            return Reject(state, c, "not_author", "That's not yours to delete.");
        }

        if (question.Used)
        {
            return Reject(state, c, "question_used", "That one's already been played.");
        }

        var next = state with { Questions = [.. state.Questions.Where(q => q.Id != c.QuestionId)] };
        return new EngineResult(next, [Progress(next)]);
    }

    private static EngineResult OnEditQuestion(GameState state, EditQuestion c)
    {
        if (state.QuestionById(c.QuestionId) is not { } question)
        {
            return Reject(state, c, "unknown_question", "That question isn't in the pot.");
        }

        if (question.AuthorId != c.ActorId)
        {
            return Reject(state, c, "not_author", "That's not yours to edit.");
        }

        // Once a question has been revealed on the host screen, rewriting it
        // would silently change what the room already saw.
        if (question.Used)
        {
            return Reject(state, c, "question_used", "That one's already been played.");
        }

        var text = (c.Text ?? string.Empty).Trim();

        if (text.Length is 0 or > Limits.MaxQuestionLength)
        {
            return Reject(state, c, "invalid_question",
                $"Keep it between 1 and {Limits.MaxQuestionLength} characters.");
        }

        if (text == question.Text)
        {
            return EngineResult.Unchanged(state);
        }

        var next = state with
        {
            Questions = [.. state.Questions.Select(q => q.Id == c.QuestionId ? q with { Text = text } : q)]
        };

        return new EngineResult(next, [Progress(next)]);
    }

    // ----------------------------------------------------------- round loop

    private static EngineResult OnStartGame(GameState state, StartGame c, EngineContext ctx)
    {
        if (RequireHost(state, c) is { } denied)
        {
            return denied;
        }

        if (state.Phase != Phase.Submission)
        {
            return Reject(state, c, "wrong_phase", "The game is already underway.");
        }

        if (state.ConnectedPlayers.Count() < Limits.MinPlayersToStart)
        {
            return Reject(state, c, "not_enough_players",
                $"You need at least {Limits.MinPlayersToStart} players to get going.");
        }

        if (state.PlayersBelowMinimum.Any())
        {
            var waiting = string.Join(", ", state.PlayersBelowMinimum.Select(p => p.Name));
            return Reject(state, c, "questions_missing", $"Still waiting on questions from {waiting}.");
        }

        return EnterSpin(state with { StartedAt = ctx.Now }, ctx);
    }

    private static EngineResult OnNextRound(GameState state, NextRound c, EngineContext ctx)
    {
        if (RequireHost(state, c) is { } denied)
        {
            return denied;
        }

        if (state.Phase != Phase.Scoreboard)
        {
            return Reject(state, c, "wrong_phase", "There's no round to advance from.");
        }

        return EnterSpin(state, ctx);
    }

    /// <summary>
    /// Decides the pairing immediately and pushes it with an animation budget.
    /// The host screen animates toward a result the server already knows — the
    /// phase advances on a server timer, never on a client callback (§2.3).
    /// </summary>
    private static EngineResult EnterSpin(GameState state, EngineContext ctx)
    {
        if (state.ConnectedPlayers.Count() < Limits.MinPlayersToStart)
        {
            return Reject(state, null, "not_enough_players", "Not enough players are still connected.");
        }

        if (state.AvailableQuestionCount == 0)
        {
            return Reject(state, null, "pool_empty", "You're out of questions — add a few more to keep going.");
        }

        var pairing = Selection.SelectPairing(state, ctx.Random);

        var round = new Round(
            Index: state.RoundsPlayed,
            ChallengerId: pairing.ChallengerId,
            TargetId: pairing.TargetId,
            Hand: [],
            ChosenQuestionId: null,
            Outcome: null);

        var next = state with
        {
            Phase = Phase.Spinning,
            CurrentRound = round,
            PhaseDeadline = ctx.Now.AddMilliseconds(Limits.SpinAnimationMs)
        };

        return new EngineResult(next,
        [
            new PhaseChanged(Phase.Spinning, next.PhaseDeadline),
            new SpinResolved(pairing.ChallengerId, pairing.TargetId, Limits.SpinAnimationMs)
        ]);
    }

    private static EngineResult OnSpinSettled(GameState state, EngineContext ctx)
    {
        if (state.Phase != Phase.Spinning || state.CurrentRound is not { } round)
        {
            return EngineResult.Unchanged(state);
        }

        var hand = Selection.DealHand(state, round.TargetId, ctx.Random);

        if (hand.Count == 0)
        {
            return EndGameInternal(state, ctx);
        }

        var next = state with
        {
            Phase = Phase.Choosing,
            CurrentRound = round with { Hand = [.. hand.Select(q => q.Id)] },
            PhaseDeadline = ctx.Now.AddSeconds(state.Settings.ChoosingTimeoutSeconds)
        };

        return new EngineResult(next,
        [
            new PhaseChanged(Phase.Choosing, next.PhaseDeadline),
            new HandDealt(round.ChallengerId, hand)
        ]);
    }

    private static EngineResult OnChooseQuestion(GameState state, ChooseQuestion c, EngineContext ctx)
    {
        if (state.Phase != Phase.Choosing || state.CurrentRound is not { } round)
        {
            return Reject(state, c, "wrong_phase", "There's nothing to choose right now.");
        }

        if (c.ActorId != round.ChallengerId)
        {
            return Reject(state, c, "not_challenger", "Only the challenger picks this round.");
        }

        if (!round.Hand.Contains(c.QuestionId) || state.QuestionById(c.QuestionId) is not { } question)
        {
            return Reject(state, c, "not_in_hand", "That card isn't in your hand.");
        }

        return Reveal(state, round, question, ctx);
    }

    private static EngineResult Reveal(GameState state, Round round, Question question, EngineContext ctx)
    {
        // Burn the chosen card. Unchosen ones stay in the pool (§6 of the plan).
        var questions = state.Questions
            .Select(q => q.Id == question.Id ? q with { Used = true } : q)
            .ToList();

        var deadline = state.Settings.TimerEnabled
            ? ctx.Now.AddSeconds(state.Settings.RevealTimerSeconds)
            : (DateTimeOffset?)null;

        var next = state with
        {
            Phase = Phase.Revealed,
            Questions = questions,
            CurrentRound = round with { ChosenQuestionId = question.Id },
            PhaseDeadline = deadline,
            FirstChallengeAt = state.FirstChallengeAt ?? ctx.Now
        };

        List<GameEvent> events =
        [
            new PhaseChanged(Phase.Revealed, deadline),
            new QuestionRevealed(question.Kind, question.Text, deadline)
        ];

        if (next.AvailableQuestionCount <= next.Settings.HandSize)
        {
            events.Add(new QuestionPoolLow(next.AvailableQuestionCount));
        }

        return new EngineResult(next, events);
    }

    private static EngineResult OnJudge(GameState state, Judge c, EngineContext ctx)
    {
        if (state.Phase is not (Phase.Revealed or Phase.Judging) || state.CurrentRound is not { } round)
        {
            return Reject(state, c, "wrong_phase", "There's nothing to judge right now.");
        }

        if (RequireHost(state, c) is { } denied)
        {
            return denied;
        }

        if (state.PlayerById(round.TargetId) is not { } target)
        {
            return Reject(state, c, "unknown_player", "That player has left the room.");
        }

        // Skip: zero points, and the question stays burned. §11 of the plan —
        // one line to soften once the skip-rate metric says whether it needs it.
        var points = c.Outcome == RoundOutcome.Success ? state.Settings.PointsForSuccess : 0;

        var next = state.WithPlayer(target with
        {
            Score = target.Score + points,
            TurnsAsTarget = target.TurnsAsTarget + 1
        });

        if (next.PlayerById(round.ChallengerId) is { } challenger)
        {
            next = next.WithPlayer(challenger with { TurnsAsChallenger = challenger.TurnsAsChallenger + 1 });
        }

        next = next with
        {
            Phase = Phase.Celebration,
            CurrentRound = round with { Outcome = c.Outcome },
            RoundsPlayed = state.RoundsPlayed + 1,
            PreviousChallengerId = round.ChallengerId,
            PreviousTargetId = round.TargetId,
            Skips = state.Skips + (c.Outcome == RoundOutcome.Skipped ? 1 : 0),
            PhaseDeadline = ctx.Now.AddMilliseconds(Limits.CelebrationMs)
        };

        return new EngineResult(next,
        [
            new PhaseChanged(Phase.Celebration, next.PhaseDeadline),
            new RoundJudged(c.Outcome, round.TargetId, points),
            new ScoresUpdated(Scores(next))
        ]);
    }

    private static EngineResult OnCelebrationFinished(GameState state)
    {
        if (state.Phase != Phase.Celebration)
        {
            return EngineResult.Unchanged(state);
        }

        var next = state with { Phase = Phase.Scoreboard, PhaseDeadline = null };
        return new EngineResult(next, [new PhaseChanged(Phase.Scoreboard, null)]);
    }

    // --------------------------------------------------------------- timers

    /// <summary>
    /// Timeouts arrive through the same mailbox as player commands, so they need
    /// no special concurrency handling. The guard on ExpectedPhase makes a
    /// late-firing timer harmless.
    /// </summary>
    private static EngineResult OnPhaseTimeout(GameState state, PhaseTimeout c, EngineContext ctx)
    {
        if (state.Phase != c.ExpectedPhase)
        {
            return EngineResult.Unchanged(state);
        }

        return state.Phase switch
        {
            Phase.Spinning => OnSpinSettled(state, ctx),
            Phase.Celebration => OnCelebrationFinished(state),

            // Nobody picked in time — pick for them rather than stalling the party.
            Phase.Choosing when state.CurrentRound is { } round && round.Hand.Count > 0 =>
                Reveal(state, round, state.QuestionById(round.Hand[ctx.Random.Next(round.Hand.Count)])!, ctx),

            // The soft reveal timer is a nudge, not a punishment (§9.4) — it just
            // moves the host screen along to the judgment buttons.
            Phase.Revealed => new EngineResult(
                state with { Phase = Phase.Judging, PhaseDeadline = null },
                [new PhaseChanged(Phase.Judging, null)]),

            _ => EngineResult.Unchanged(state)
        };
    }

    // ----------------------------------------------------------------- end

    private static EngineResult OnEndGame(GameState state, EndGame c, EngineContext ctx)
    {
        if (RequireHost(state, c) is { } denied)
        {
            return denied;
        }

        return state.Phase == Phase.Ended
            ? EngineResult.Unchanged(state)
            : EndGameInternal(state, ctx);
    }

    private static EngineResult EndGameInternal(GameState state, EngineContext ctx)
    {
        var standings = state.PlayingPlayers
            .OrderByDescending(p => p.Score)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var next = state with
        {
            Phase = Phase.Ended,
            EndedAt = ctx.Now,
            CurrentRound = null,
            PhaseDeadline = null
        };

        return new EngineResult(next, [new PhaseChanged(Phase.Ended, null), new GameEnded(standings)]);
    }

    // -------------------------------------------------------------- helpers

    private static EngineResult? RequireHost(GameState state, Command c) =>
        c.ActorId is { } actor && state.PlayerById(actor)?.IsHost == true
            ? null
            : Reject(state, c, "not_host", "Only the host can do that.");

    private static EngineResult Reject(GameState state, Command? c, string code, string message) =>
        new(state, [new CommandRejected(c?.ActorId, code, message)]);

    private static SubmissionProgress Progress(GameState state) =>
        new(state.Players.ToDictionary(p => p.Id, p => state.QuestionsBy(p.Id)), state.Questions.Count);

    private static Dictionary<Guid, int> Scores(GameState state) =>
        state.Players.ToDictionary(p => p.Id, p => p.Score);
}
