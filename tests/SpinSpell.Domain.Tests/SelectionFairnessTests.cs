using SpinSpell.Domain;

namespace SpinSpell.Domain.Tests;

/// <summary>
/// The reason the engine is a pure function.
///
/// A rotation bug does not look like a bug — it looks like bad luck, and it
/// looks like bad luck for months. You cannot catch it by playing three rounds,
/// so it gets caught here instead, across tens of thousands of simulated ones.
/// </summary>
public class SelectionFairnessTests
{
    private const int Rounds = 10_000;

    /// <summary>Stable ids, so two runs of the same seed are actually comparable.</summary>
    private static Guid IdFor(int index) => new(index, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);

    private static GameState RoomOf(int playerCount)
    {
        var now = DateTimeOffset.UnixEpoch;
        // P0 owns the room, and is in the pool exactly like everyone else —
        // "nobody gets left out" has to hold for them too.
        var players = Enumerable.Range(0, playerCount)
            .Select(i => Player.Create(IdFor(i), $"P{i}", i, isOwner: i == 0, now))
            .ToList();

        return GameState.NewRoom(Guid.NewGuid(), "TEST01", new RoomSettings(), now) with
        {
            Players = players
        };
    }

    /// <summary>Applies the counter updates the engine would make after a judged round.</summary>
    private static GameState Advance(GameState state, Pairing pairing)
    {
        var challenger = state.PlayerById(pairing.ChallengerId)!;
        var target = state.PlayerById(pairing.TargetId)!;

        return state
            .WithPlayer(challenger with { TurnsAsChallenger = challenger.TurnsAsChallenger + 1 })
            .WithPlayer(target with { TurnsAsTarget = target.TurnsAsTarget + 1 })
            with
        {
            PreviousChallengerId = pairing.ChallengerId,
            PreviousTargetId = pairing.TargetId
        };
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    public void Nobody_gets_left_out_over_a_long_session(int playerCount)
    {
        var state = RoomOf(playerCount);
        var random = new SeededRandom(seed: 20260816);

        for (var i = 0; i < Rounds; i++)
        {
            state = Advance(state, Selection.SelectPairing(state, random));
        }

        var expected = (double)Rounds / playerCount;
        var targetTurns = state.Players.Select(p => p.TurnsAsTarget).ToList();

        // Every player's share of the spotlight within 10% of an even split.
        foreach (var turns in targetTurns)
        {
            Assert.InRange(turns, expected * 0.90, expected * 1.10);
        }

        // And the same for who gets to pick the question.
        foreach (var turns in state.Players.Select(p => p.TurnsAsChallenger))
        {
            Assert.InRange(turns, expected * 0.90, expected * 1.10);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(9)]
    public void Challenger_and_target_are_never_the_same_person(int playerCount)
    {
        var state = RoomOf(playerCount);
        var random = new SeededRandom(seed: 7);

        for (var i = 0; i < Rounds; i++)
        {
            var pairing = Selection.SelectPairing(state, random);
            Assert.NotEqual(pairing.ChallengerId, pairing.TargetId);
            state = Advance(state, pairing);
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(10)]
    public void The_same_pairing_never_repeats_back_to_back(int playerCount)
    {
        var state = RoomOf(playerCount);
        var random = new SeededRandom(seed: 99);
        Pairing? previous = null;

        for (var i = 0; i < Rounds; i++)
        {
            var pairing = Selection.SelectPairing(state, random);
            Assert.NotEqual(previous, pairing);
            previous = pairing;
            state = Advance(state, pairing);
        }
    }

    [Fact]
    public void Two_players_alternate_cleanly_rather_than_deadlocking()
    {
        var state = RoomOf(2);
        var random = new SeededRandom(seed: 3);
        Pairing? previous = null;

        for (var i = 0; i < 500; i++)
        {
            var pairing = Selection.SelectPairing(state, random);

            // With only two people the exclusion rules are maximally constrained;
            // the relax-if-empty ladder is what keeps this from having no answer.
            Assert.NotEqual(pairing.ChallengerId, pairing.TargetId);
            Assert.NotEqual(previous, pairing);

            previous = pairing;
            state = Advance(state, pairing);
        }

        Assert.Equal(250, state.Players[0].TurnsAsTarget);
        Assert.Equal(250, state.Players[1].TurnsAsTarget);
    }

    [Fact]
    public void Disconnected_players_are_not_selected()
    {
        var state = RoomOf(5);
        var absent = state.Players[2];
        state = state.WithPlayer(absent with { Connected = false });

        var random = new SeededRandom(seed: 11);

        for (var i = 0; i < 1_000; i++)
        {
            var pairing = Selection.SelectPairing(state, random);
            Assert.NotEqual(absent.Id, pairing.ChallengerId);
            Assert.NotEqual(absent.Id, pairing.TargetId);
            state = Advance(state, pairing);
        }
    }

    [Fact]
    public void A_player_joining_mid_session_is_pulled_in_straight_away()
    {
        // The scenario that actually happens: someone shows up late to a party
        // game already in progress. They should be in the thick of it within a
        // few rounds, not politely queued behind everyone else.
        var state = RoomOf(4);
        var random = new SeededRandom(seed: 42);

        for (var i = 0; i < 20; i++)
        {
            state = Advance(state, Selection.SelectPairing(state, random));
        }

        var latecomer = Player.Create(IdFor(99), "Late", 4, isOwner: false, DateTimeOffset.UnixEpoch);
        state = state with { Players = [.. state.Players, latecomer] };

        for (var i = 0; i < 10; i++)
        {
            state = Advance(state, Selection.SelectPairing(state, random));
        }

        // An even split of 10 rounds across 5 players would be 2. Inverse-turn
        // weighting should have given them substantially more than that.
        var theirTurns = state.PlayerById(latecomer.Id)!.TurnsAsTarget;

        Assert.True(theirTurns >= 4,
            $"Latecomer got only {theirTurns} of the first 10 rounds after joining — catch-up weighting is too weak.");
    }

    [Fact]
    public void The_gap_left_by_a_late_join_narrows_rather_than_persisting()
    {
        var state = RoomOf(4);
        var random = new SeededRandom(seed: 42);

        for (var i = 0; i < 400; i++)
        {
            state = Advance(state, Selection.SelectPairing(state, random));
        }

        var latecomer = Player.Create(IdFor(99), "Late", 4, isOwner: false, DateTimeOffset.UnixEpoch);
        var gapOnArrival = state.Players.Average(p => p.TurnsAsTarget);
        state = state with { Players = [.. state.Players, latecomer] };

        for (var i = 0; i < 400; i++)
        {
            state = Advance(state, Selection.SelectPairing(state, random));
        }

        var theirTurns = state.PlayerById(latecomer.Id)!.TurnsAsTarget;
        var averageOfOthers = state.Players.Where(p => p.Id != latecomer.Id).Average(p => p.TurnsAsTarget);

        // Convergence is asymptotic by design: the weighting eases off as the gap
        // closes, which keeps the spin unpredictable instead of turning into
        // "obviously the new person again". What matters is the direction.
        Assert.True(averageOfOthers - theirTurns < gapOnArrival,
            $"Gap went from {gapOnArrival:F1} to {averageOfOthers - theirTurns:F1} — it should be closing, not widening.");

        // And they must be well clear of a merely-even share of the new rounds.
        Assert.True(theirTurns > 400 / 5.0,
            $"Latecomer got {theirTurns}, no better than an even split would have given them.");
    }

    [Fact]
    public void Selection_is_reproducible_from_a_seed()
    {
        // The property the whole architecture rests on: same seed, same game.
        static List<Pairing> Run()
        {
            var state = RoomOf(5);
            var random = new SeededRandom(seed: 12345);
            var pairings = new List<Pairing>();

            for (var i = 0; i < 200; i++)
            {
                var pairing = Selection.SelectPairing(state, random);
                pairings.Add(pairing);
                state = Advance(state, pairing);
            }
            return pairings;
        }

        Assert.Equal(Run(), Run());
    }
}
