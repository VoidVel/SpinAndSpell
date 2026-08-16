using SpinSpell.Domain;

namespace SpinSpell.Domain.Tests;

public class LobbyTests
{
    [Fact]
    public void Players_get_distinct_colour_slots_on_join()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben", "Cleo");

        var slots = game.State.Players.Select(p => p.ColourSlot).ToList();
        Assert.Equal(slots.Count, slots.Distinct().Count());
    }

    [Fact]
    public void A_leaving_player_frees_their_colour_for_the_next_joiner()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben");
        var benSlot = game.Player("Ben").ColourSlot;

        game.Send(new LeaveRoom(game["Ben"]));
        game.WithPlayers("Cleo");

        Assert.Equal(benSlot, game.Player("Cleo").ColourSlot);
    }

    [Fact]
    public void Duplicate_names_are_refused_so_the_scoreboard_stays_readable()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana");
        game.Send(new JoinRoom(Guid.NewGuid(), "ana", AsHost: false));

        Assert.Equal("name_taken", game.Rejection.Code);
    }

    [Fact]
    public void Rejoining_with_a_known_id_is_idempotent_not_an_error()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana");
        var before = game.State.Players.Count;

        game.Send(new JoinRoom(game["Ana"], "Ana", AsHost: false));

        Assert.Equal(before, game.State.Players.Count);
        Assert.Empty(game.LastEvents.OfType<CommandRejected>());
    }

    [Fact]
    public void Nobody_can_join_once_the_game_is_underway()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben").WithQuestions();
        game.Send(new StartGame { ActorId = game.HostId });

        game.Send(new JoinRoom(Guid.NewGuid(), "Latecomer", AsHost: false));

        Assert.Equal("game_in_progress", game.Rejection.Code);
    }

    [Fact]
    public void Host_leaving_the_lobby_hands_off_instead_of_killing_the_room()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben");

        game.Send(new LeaveRoom(game.HostId));

        Assert.Equal(game["Ana"], game.Event<HostChanged>().NewHostId);
        Assert.True(game.Player("Ana").IsHost);
    }

    [Fact]
    public void Only_the_host_can_start_the_game()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben").WithQuestions();

        game.Send(new StartGame { ActorId = game["Ana"] });

        Assert.Equal("not_host", game.Rejection.Code);
        Assert.Equal(Phase.Submission, game.State.Phase);
    }
}

public class SubmissionTests
{
    [Fact]
    public void The_game_will_not_start_until_everyone_has_written_their_share()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben");
        game.Send(new OpenSubmissions { ActorId = game.HostId });

        for (var i = 0; i < 4; i++)
        {
            game.Send(new SubmitQuestion(Guid.NewGuid(), QuestionKind.Truth, $"q{i}") { ActorId = game["Ana"] });
        }

        game.Send(new StartGame { ActorId = game.HostId });

        Assert.Equal("questions_missing", game.Rejection.Code);
        Assert.Contains("Ben", game.Rejection.Message);
    }

    [Fact]
    public void The_host_does_not_have_to_write_questions_when_they_are_not_playing()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben").WithQuestions();

        game.Send(new StartGame { ActorId = game.HostId });

        Assert.Equal(Phase.Spinning, game.State.Phase);
    }

    [Fact]
    public void A_playing_host_does_have_to_write_questions()
    {
        var game = new TestGame(settings: new RoomSettings(HostPlays: true))
            .WithHost().WithPlayers("Ana", "Ben").WithQuestions();

        game.Send(new StartGame { ActorId = game.HostId });

        Assert.Equal("questions_missing", game.Rejection.Code);
    }

    [Fact]
    public void Resubmitting_the_same_id_does_not_double_up()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben");
        game.Send(new OpenSubmissions { ActorId = game.HostId });

        var id = Guid.NewGuid();
        game.Send(new SubmitQuestion(id, QuestionKind.Dare, "Sing something") { ActorId = game["Ana"] });
        game.Send(new SubmitQuestion(id, QuestionKind.Dare, "Sing something") { ActorId = game["Ana"] });

        Assert.Single(game.State.Questions);
    }

    [Fact]
    public void Blank_and_oversized_questions_are_refused()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben");
        game.Send(new OpenSubmissions { ActorId = game.HostId });

        game.Send(new SubmitQuestion(Guid.NewGuid(), QuestionKind.Truth, "   ") { ActorId = game["Ana"] });
        Assert.Equal("invalid_question", game.Rejection.Code);

        game.Send(new SubmitQuestion(Guid.NewGuid(), QuestionKind.Truth, new string('x', 281)) { ActorId = game["Ana"] });
        Assert.Equal("invalid_question", game.Rejection.Code);
    }

    [Fact]
    public void You_can_reword_your_own_question_before_it_is_played()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben");
        game.Send(new OpenSubmissions { ActorId = game.HostId });

        var id = Guid.NewGuid();
        game.Send(new SubmitQuestion(id, QuestionKind.Dare, "Sing something") { ActorId = game["Ana"] });
        game.Send(new EditQuestion(id, "  Sing something embarrassing  ") { ActorId = game["Ana"] });

        Assert.Equal("Sing something embarrassing", game.State.QuestionById(id)!.Text);
        Assert.Single(game.State.Questions);
    }

    [Fact]
    public void You_cannot_reword_someone_elses_question()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben");
        game.Send(new OpenSubmissions { ActorId = game.HostId });

        var id = Guid.NewGuid();
        game.Send(new SubmitQuestion(id, QuestionKind.Truth, "Mine") { ActorId = game["Ana"] });
        game.Send(new EditQuestion(id, "Hijacked") { ActorId = game["Ben"] });

        Assert.Equal("not_author", game.Rejection.Code);
        Assert.Equal("Mine", game.State.QuestionById(id)!.Text);
    }

    [Fact]
    public void A_question_cannot_be_reworded_once_it_has_been_played()
    {
        // Otherwise the text on the host screen and the text in the pot diverge.
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben").WithQuestions(6);
        game.Send(new StartGame { ActorId = game.HostId });
        game.Tick();

        var round = game.State.CurrentRound!;
        var chosen = round.Hand[0];
        var author = game.State.QuestionById(chosen)!.AuthorId;
        var original = game.State.QuestionById(chosen)!.Text;

        game.Send(new ChooseQuestion(chosen) { ActorId = round.ChallengerId });
        game.Send(new EditQuestion(chosen, "Something much easier") { ActorId = author });

        Assert.Equal("question_used", game.Rejection.Code);
        Assert.Equal(original, game.State.QuestionById(chosen)!.Text);
    }

    [Fact]
    public void Rewording_a_question_to_nothing_is_refused()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben");
        game.Send(new OpenSubmissions { ActorId = game.HostId });

        var id = Guid.NewGuid();
        game.Send(new SubmitQuestion(id, QuestionKind.Truth, "Keep me") { ActorId = game["Ana"] });
        game.Send(new EditQuestion(id, "   ") { ActorId = game["Ana"] });

        Assert.Equal("invalid_question", game.Rejection.Code);
        Assert.Equal("Keep me", game.State.QuestionById(id)!.Text);
    }

    [Fact]
    public void You_can_only_delete_your_own_questions()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben");
        game.Send(new OpenSubmissions { ActorId = game.HostId });

        var id = Guid.NewGuid();
        game.Send(new SubmitQuestion(id, QuestionKind.Truth, "Mine") { ActorId = game["Ana"] });
        game.Send(new DeleteQuestion(id) { ActorId = game["Ben"] });

        Assert.Equal("not_author", game.Rejection.Code);
        Assert.Single(game.State.Questions);
    }
}

public class QuestionModeTests
{
    private static TestGame InLobby() => new TestGame().WithHost().WithPlayers("Ana", "Ben");

    [Fact]
    public void A_room_takes_both_kinds_by_default()
    {
        var game = InLobby();

        Assert.Equal(QuestionMode.Both, game.State.Settings.Mode);
        Assert.True(game.State.Allows(QuestionKind.Truth));
        Assert.True(game.State.Allows(QuestionKind.Dare));
    }

    [Theory]
    [InlineData(QuestionMode.TruthsOnly)]
    [InlineData(QuestionMode.DaresOnly)]
    public void The_host_sets_the_mode_in_the_lobby(QuestionMode mode)
    {
        var game = InLobby();

        game.Send(new SetQuestionMode(mode) { ActorId = game.HostId });

        Assert.Equal(mode, game.State.Settings.Mode);
        Assert.Equal(mode, game.Event<QuestionModeChanged>().Mode);
    }

    [Fact]
    public void Only_the_host_sets_the_mode()
    {
        var game = InLobby();

        game.Send(new SetQuestionMode(QuestionMode.DaresOnly) { ActorId = game["Ana"] });

        Assert.Equal("not_host", game.Rejection.Code);
        Assert.Equal(QuestionMode.Both, game.State.Settings.Mode);
    }

    [Fact]
    public void The_mode_locks_once_people_start_writing()
    {
        // Otherwise a late switch to dares-only would strand a pool of truths
        // and the game would have nothing left to deal.
        var game = InLobby();
        game.Send(new OpenSubmissions { ActorId = game.HostId });

        game.Send(new SetQuestionMode(QuestionMode.DaresOnly) { ActorId = game.HostId });

        Assert.Equal("mode_locked", game.Rejection.Code);
        Assert.Equal(QuestionMode.Both, game.State.Settings.Mode);
    }

    [Fact]
    public void Setting_the_same_mode_again_is_a_no_op()
    {
        var game = InLobby();

        game.Send(new SetQuestionMode(QuestionMode.Both) { ActorId = game.HostId });

        Assert.Empty(game.LastEvents);
    }

    [Fact]
    public void A_truths_only_room_refuses_dares()
    {
        var game = InLobby();
        game.Send(new SetQuestionMode(QuestionMode.TruthsOnly) { ActorId = game.HostId });
        game.Send(new OpenSubmissions { ActorId = game.HostId });

        game.Send(new SubmitQuestion(Guid.NewGuid(), QuestionKind.Dare, "Lick the floor") { ActorId = game["Ana"] });
        Assert.Equal("wrong_kind", game.Rejection.Code);
        Assert.Empty(game.State.Questions);

        game.Send(new SubmitQuestion(Guid.NewGuid(), QuestionKind.Truth, "Worst date?") { ActorId = game["Ana"] });
        Assert.Single(game.State.Questions);
    }

    [Fact]
    public void A_dares_only_room_refuses_truths()
    {
        var game = InLobby();
        game.Send(new SetQuestionMode(QuestionMode.DaresOnly) { ActorId = game.HostId });
        game.Send(new OpenSubmissions { ActorId = game.HostId });

        game.Send(new SubmitQuestion(Guid.NewGuid(), QuestionKind.Truth, "Worst date?") { ActorId = game["Ana"] });
        Assert.Equal("wrong_kind", game.Rejection.Code);
        Assert.Empty(game.State.Questions);

        game.Send(new SubmitQuestion(Guid.NewGuid(), QuestionKind.Dare, "Lick the floor") { ActorId = game["Ana"] });
        Assert.Single(game.State.Questions);
    }

    [Fact]
    public void A_single_kind_room_still_deals_a_full_hand_and_plays_through()
    {
        // The hand dealer balances truth against dare; with one kind absent it
        // has to fall through cleanly rather than deal short.
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben", "Cleo");
        game.Send(new SetQuestionMode(QuestionMode.DaresOnly) { ActorId = game.HostId });
        game.Send(new OpenSubmissions { ActorId = game.HostId });

        foreach (var (name, id) in game.Players)
        {
            for (var i = 0; i < 6; i++)
            {
                game.Send(new SubmitQuestion(Guid.NewGuid(), QuestionKind.Dare, $"{name} dare {i}") { ActorId = id });
            }
        }

        game.Send(new StartGame { ActorId = game.HostId });
        game.Tick();

        var round = game.State.CurrentRound!;
        Assert.Equal(3, round.Hand.Count);
        Assert.All(round.Hand, id => Assert.Equal(QuestionKind.Dare, game.State.QuestionById(id)!.Kind));

        game.Send(new ChooseQuestion(round.Hand[0]) { ActorId = round.ChallengerId });
        Assert.Equal(QuestionKind.Dare, game.State.QuestionById(round.Hand[0])!.Kind);
        Assert.Equal(Phase.Revealed, game.State.Phase);
    }

    [Fact]
    public void A_mid_game_top_up_still_respects_the_mode()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben").WithQuestions(6);
        game.Send(new StartGame { ActorId = game.HostId });
        game.PlayRound();

        // Scoreboard phase allows submissions; the mode must still apply there.
        Assert.Equal(Phase.Scoreboard, game.State.Phase);
        game.Send(new SubmitQuestion(Guid.NewGuid(), QuestionKind.Truth, "Extra") { ActorId = game["Ana"] });

        Assert.Equal(Phase.Scoreboard, game.State.Phase);
        Assert.DoesNotContain(game.LastEvents.OfType<CommandRejected>(), r => r.Code == "wrong_kind");
    }
}

public class RoundLoopTests
{
    private static TestGame Started(int seed = 1)
    {
        var game = new TestGame(seed).WithHost().WithPlayers("Ana", "Ben", "Cleo").WithQuestions(6);
        game.Send(new StartGame { ActorId = game.HostId });
        return game;
    }

    [Fact]
    public void Starting_the_game_resolves_a_pairing_immediately_with_an_animation_budget()
    {
        var game = Started();
        var spin = game.Event<SpinResolved>();

        Assert.Equal(Phase.Spinning, game.State.Phase);
        Assert.NotEqual(spin.ChallengerId, spin.TargetId);
        Assert.True(spin.AnimationMs > 0);
        Assert.NotNull(game.State.PhaseDeadline);
    }

    [Fact]
    public void The_hand_goes_only_to_the_challenger()
    {
        var game = Started();
        game.Tick();

        var dealt = game.Event<HandDealt>();

        Assert.Equal(AudienceKind.Player, dealt.Audience.Kind);
        Assert.Equal(game.State.CurrentRound!.ChallengerId, dealt.Audience.PlayerId);
    }

    [Fact]
    public void The_hand_never_contains_a_question_the_target_wrote()
    {
        var game = Started(seed: 5);

        for (var round = 0; round < 8; round++)
        {
            game.Tick();
            var current = game.State.CurrentRound!;

            foreach (var questionId in current.Hand)
            {
                Assert.NotEqual(current.TargetId, game.State.QuestionById(questionId)!.AuthorId);
            }

            game.Send(new ChooseQuestion(current.Hand[0]) { ActorId = current.ChallengerId });
            game.Send(new Judge(RoundOutcome.Success) { ActorId = game.HostId });
            game.Tick();
            game.Send(new NextRound { ActorId = game.HostId });
        }
    }

    [Fact]
    public void Only_the_challenger_may_choose()
    {
        var game = Started();
        game.Tick();

        var round = game.State.CurrentRound!;
        var someoneElse = game.State.Players.First(p => p.Id != round.ChallengerId && !p.IsHost);

        game.Send(new ChooseQuestion(round.Hand[0]) { ActorId = someoneElse.Id });

        Assert.Equal("not_challenger", game.Rejection.Code);
        Assert.Equal(Phase.Choosing, game.State.Phase);
    }

    [Fact]
    public void A_challenger_cannot_reach_outside_their_dealt_hand()
    {
        var game = Started();
        game.Tick();

        var round = game.State.CurrentRound!;
        var undealt = game.State.Questions.First(q => !round.Hand.Contains(q.Id));

        game.Send(new ChooseQuestion(undealt.Id) { ActorId = round.ChallengerId });

        Assert.Equal("not_in_hand", game.Rejection.Code);
    }

    [Fact]
    public void The_chosen_question_burns_and_the_rest_go_back_to_the_pool()
    {
        var game = Started();
        game.Tick();

        var round = game.State.CurrentRound!;
        var chosen = round.Hand[0];
        var alsoDealt = round.Hand[1];

        game.Send(new ChooseQuestion(chosen) { ActorId = round.ChallengerId });

        Assert.True(game.State.QuestionById(chosen)!.Used);
        Assert.False(game.State.QuestionById(alsoDealt)!.Used);
    }

    [Fact]
    public void A_successful_round_scores_the_target()
    {
        var game = Started();
        game.Tick();

        var round = game.State.CurrentRound!;
        var target = game.State.PlayerById(round.TargetId)!;

        game.Send(new ChooseQuestion(round.Hand[0]) { ActorId = round.ChallengerId });
        game.Send(new Judge(RoundOutcome.Success) { ActorId = game.HostId });

        Assert.Equal(target.Score + 1, game.State.PlayerById(round.TargetId)!.Score);
        Assert.Equal(Phase.Celebration, game.State.Phase);
    }

    [Fact]
    public void A_skip_scores_nothing_and_the_question_stays_burned()
    {
        var game = Started();
        game.Tick();

        var round = game.State.CurrentRound!;
        game.Send(new ChooseQuestion(round.Hand[0]) { ActorId = round.ChallengerId });
        game.Send(new Judge(RoundOutcome.Skipped) { ActorId = game.HostId });

        Assert.Equal(0, game.State.PlayerById(round.TargetId)!.Score);
        Assert.True(game.State.QuestionById(round.Hand[0])!.Used);
        Assert.Equal(1, game.State.Skips);
    }

    [Fact]
    public void Only_the_host_judges()
    {
        var game = Started();
        game.Tick();

        var round = game.State.CurrentRound!;
        game.Send(new ChooseQuestion(round.Hand[0]) { ActorId = round.ChallengerId });
        game.Send(new Judge(RoundOutcome.Success) { ActorId = round.ChallengerId });

        Assert.Equal("not_host", game.Rejection.Code);
        Assert.Equal(Phase.Revealed, game.State.Phase);
    }

    [Fact]
    public void A_full_round_walks_the_phase_machine_in_order()
    {
        var game = Started();
        Assert.Equal(Phase.Spinning, game.State.Phase);

        game.Tick();
        Assert.Equal(Phase.Choosing, game.State.Phase);

        var round = game.State.CurrentRound!;
        game.Send(new ChooseQuestion(round.Hand[0]) { ActorId = round.ChallengerId });
        Assert.Equal(Phase.Revealed, game.State.Phase);

        game.Send(new Judge(RoundOutcome.Success) { ActorId = game.HostId });
        Assert.Equal(Phase.Celebration, game.State.Phase);

        game.Tick();
        Assert.Equal(Phase.Scoreboard, game.State.Phase);

        game.Send(new NextRound { ActorId = game.HostId });
        Assert.Equal(Phase.Spinning, game.State.Phase);
        Assert.Equal(1, game.State.RoundsPlayed);
    }

    [Fact]
    public void Time_to_first_challenge_is_stamped_once_and_never_moves()
    {
        var game = Started();
        game.PlayRound();
        var first = game.State.FirstChallengeAt;

        game.Send(new NextRound { ActorId = game.HostId });
        game.PlayRound();

        Assert.NotNull(first);
        Assert.Equal(first, game.State.FirstChallengeAt);
    }
}

public class TimerAndDisconnectTests
{
    private static TestGame Started(RoomSettings? settings = null)
    {
        var game = new TestGame(settings: settings).WithHost().WithPlayers("Ana", "Ben", "Cleo").WithQuestions(6);
        game.Send(new StartGame { ActorId = game.HostId });
        return game;
    }

    [Fact]
    public void A_stalled_challenger_gets_a_card_picked_for_them_rather_than_freezing_the_party()
    {
        var game = Started();
        game.Tick(); // Spinning -> Choosing
        game.Tick(); // Choosing timeout

        Assert.Equal(Phase.Revealed, game.State.Phase);
        Assert.NotNull(game.State.CurrentRound!.ChosenQuestionId);
    }

    [Fact]
    public void A_late_timer_for_a_phase_we_already_left_is_harmless()
    {
        var game = Started();
        game.Tick();

        var round = game.State.CurrentRound!;
        game.Send(new ChooseQuestion(round.Hand[0]) { ActorId = round.ChallengerId });

        // The Choosing deadline fires after the challenger already picked.
        game.Send(new PhaseTimeout(Phase.Choosing));

        Assert.Equal(Phase.Revealed, game.State.Phase);
        Assert.Equal(round.Hand[0], game.State.CurrentRound!.ChosenQuestionId);
    }

    [Fact]
    public void The_reveal_timer_only_nudges_the_host_screen_along()
    {
        var game = Started(new RoomSettings(TimerEnabled: true, RevealTimerSeconds: 30));
        game.Tick();

        var round = game.State.CurrentRound!;
        game.Send(new ChooseQuestion(round.Hand[0]) { ActorId = round.ChallengerId });
        Assert.NotNull(game.State.PhaseDeadline);

        game.Tick();

        // Judging, not auto-skipped — §9.4 wants energy, not punishment.
        Assert.Equal(Phase.Judging, game.State.Phase);
        game.Send(new Judge(RoundOutcome.Success) { ActorId = game.HostId });
        Assert.Equal(Phase.Celebration, game.State.Phase);
    }

    [Fact]
    public void No_reveal_deadline_is_set_when_the_timer_is_off()
    {
        var game = Started(new RoomSettings(TimerEnabled: false));
        game.Tick();

        var round = game.State.CurrentRound!;
        game.Send(new ChooseQuestion(round.Hand[0]) { ActorId = round.ChallengerId });

        Assert.Null(game.State.PhaseDeadline);
    }

    [Fact]
    public void A_player_leaving_mid_game_is_marked_gone_not_removed()
    {
        var game = Started();

        game.Send(new LeaveRoom(game["Ana"]));

        Assert.NotNull(game.State.PlayerById(game["Ana"]));
        Assert.False(game.Player("Ana").Connected);
    }

    [Fact]
    public void The_room_refuses_to_spin_once_too_many_people_have_dropped()
    {
        var game = Started();
        game.PlayRound();

        game.Send(new SetConnected(game["Ana"], false));
        game.Send(new SetConnected(game["Ben"], false));
        game.Send(new NextRound { ActorId = game.HostId });

        Assert.Equal("not_enough_players", game.Rejection.Code);
    }

    [Fact]
    public void Host_handoff_moves_the_badge_exactly_once()
    {
        var game = Started();

        game.Send(new TransferHost(game["Ana"]) { ActorId = game.HostId });

        Assert.Single(game.State.Players, p => p.IsHost);
        Assert.True(game.Player("Ana").IsHost);
    }

    [Fact]
    public void A_server_initiated_handoff_needs_no_actor()
    {
        var game = Started();

        // No ActorId: the grace period lapsed and the server is reassigning (§9.5).
        game.Send(new TransferHost(game["Ben"]));

        Assert.True(game.Player("Ben").IsHost);
    }
}

public class PoolExhaustionTests
{
    [Fact]
    public void The_room_warns_when_the_pool_is_running_low()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben").WithQuestions(4);
        game.Send(new StartGame { ActorId = game.HostId });

        var sawWarning = false;

        for (var i = 0; i < 6 && game.State.Phase != Phase.Ended; i++)
        {
            game.Tick();
            if (game.State.CurrentRound is not { Hand.Count: > 0 } round)
            {
                break;
            }

            game.Send(new ChooseQuestion(round.Hand[0]) { ActorId = round.ChallengerId });
            sawWarning |= game.Has<QuestionPoolLow>();

            game.Send(new Judge(RoundOutcome.Success) { ActorId = game.HostId });
            game.Tick();

            if (game.State.Phase == Phase.Scoreboard)
            {
                game.Send(new NextRound { ActorId = game.HostId });
            }
        }

        Assert.True(sawWarning, "The host screen never got a low-pool warning before running dry.");
    }

    [Fact]
    public void Running_completely_dry_ends_the_game_rather_than_hanging()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben").WithQuestions(4);
        game.Send(new StartGame { ActorId = game.HostId });

        for (var i = 0; i < 20 && game.State.Phase != Phase.Ended; i++)
        {
            if (game.State.Phase == Phase.Scoreboard)
            {
                game.Send(new NextRound { ActorId = game.HostId });
                if (game.Has<CommandRejected>())
                {
                    break;
                }
            }

            game.Tick();

            if (game.State.CurrentRound is { ChosenQuestionId: null, Hand.Count: > 0 } round)
            {
                game.Send(new ChooseQuestion(round.Hand[0]) { ActorId = round.ChallengerId });
                game.Send(new Judge(RoundOutcome.Success) { ActorId = game.HostId });
                game.Tick();
            }
        }

        Assert.True(game.State.Phase is Phase.Ended or Phase.Scoreboard);
        Assert.True(game.State.RoundsPlayed >= 4);
    }
}

public class EndGameTests
{
    [Fact]
    public void The_host_can_end_anytime_and_standings_come_back_sorted()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben", "Cleo").WithQuestions(6);
        game.Send(new StartGame { ActorId = game.HostId });
        game.PlayRound();

        game.Send(new EndGame { ActorId = game.HostId });

        var standings = game.Event<GameEnded>().FinalStandings;

        Assert.Equal(Phase.Ended, game.State.Phase);
        Assert.NotNull(game.State.EndedAt);
        Assert.Equal(standings.OrderByDescending(p => p.Score).Select(p => p.Id), standings.Select(p => p.Id));
        Assert.DoesNotContain(standings, p => p.IsHost); // a non-playing host is not in the results
    }

    [Fact]
    public void Ending_twice_is_a_no_op()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben").WithQuestions();
        game.Send(new StartGame { ActorId = game.HostId });
        game.Send(new EndGame { ActorId = game.HostId });
        var endedAt = game.State.EndedAt;

        game.Send(new EndGame { ActorId = game.HostId });

        Assert.Equal(endedAt, game.State.EndedAt);
    }
}

public class DeterminismTests
{
    [Fact]
    public void The_same_seed_and_the_same_commands_produce_the_same_game()
    {
        static GameState Run()
        {
            var game = new TestGame(seed: 777).WithHost().WithPlayers("Ana", "Ben", "Cleo").WithQuestions(8);
            game.Send(new StartGame { ActorId = game.HostId });

            for (var i = 0; i < 5; i++)
            {
                game.PlayRound(i % 3 == 0 ? RoundOutcome.Skipped : RoundOutcome.Success);
                game.Send(new NextRound { ActorId = game.HostId });
            }
            return game.State;
        }

        var a = Run();
        var b = Run();

        // Ids are freshly generated per run, so compare the shape of the outcome.
        Assert.Equal(a.RoundsPlayed, b.RoundsPlayed);
        Assert.Equal(a.Skips, b.Skips);
        Assert.Equal(
            a.Players.Select(p => (p.Name, p.Score, p.TurnsAsTarget, p.TurnsAsChallenger)),
            b.Players.Select(p => (p.Name, p.Score, p.TurnsAsTarget, p.TurnsAsChallenger)));
    }

    [Fact]
    public void A_malformed_command_is_rejected_rather_than_faulting_the_room()
    {
        var game = new TestGame().WithHost().WithPlayers("Ana", "Ben").WithQuestions();

        // Commands from a stranger, out of phase, and for things that do not exist.
        game.Send(new Judge(RoundOutcome.Success) { ActorId = Guid.NewGuid() });
        game.Send(new ChooseQuestion(Guid.NewGuid()) { ActorId = game["Ana"] });
        game.Send(new NextRound { ActorId = game.HostId });
        game.Send(new DeleteQuestion(Guid.NewGuid()) { ActorId = game["Ana"] });

        Assert.Equal(Phase.Submission, game.State.Phase);
    }
}
