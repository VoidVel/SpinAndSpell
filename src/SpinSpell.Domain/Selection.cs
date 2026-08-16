namespace SpinSpell.Domain;

public sealed record Pairing(Guid ChallengerId, Guid TargetId);

/// <summary>
/// Fair rotation (§9.1-9.2 of the source of truth) and hand dealing (§4).
///
/// The rules, in priority order — each is relaxed only if honouring it would
/// leave no candidates at all, which is what makes this work at 2 players as
/// well as at 10:
///   1. Challenger and target must differ.            (never relaxed)
///   2. Only connected players are eligible.          (never relaxed)
///   3. Don't repeat the immediately previous pairing.
///   4. Don't reuse the previous target as target, or previous challenger as challenger.
///   5. Weight toward whoever has had fewer turns.
///
/// THE TARGET IS PICKED FIRST, AND DELIBERATELY SO. Being the target is what
/// "getting a turn" means in the room — you perform, everyone watches. Being the
/// challenger is a side role. Picking the challenger first (the obvious reading
/// of the spin animation) quietly wrecks fairness: a player who is behind on
/// turns wins the challenger seat on catch-up weighting, which then *disqualifies*
/// them from the target seat, so the two catch-up rules cancel each other out and
/// the person being left out stays left out. Caught by
/// SelectionFairnessTests.A_player_joining_mid_session_is_pulled_in_straight_away.
/// </summary>
public static class Selection
{
    public static Pairing SelectPairing(GameState state, IRandomSource random)
    {
        var eligible = state.ConnectedPlayers.ToList();

        if (eligible.Count < 2)
        {
            throw new InvalidOperationException("A pairing needs at least two connected players.");
        }

        var target = PickTarget(state, eligible, random);
        var challenger = PickChallenger(state, eligible, target, random);

        return new Pairing(challenger.Id, target.Id);
    }

    private static Player PickTarget(GameState state, List<Player> eligible, IRandomSource random)
    {
        // Rule 4: avoid the back-to-back target, but only while someone else can take it.
        var pool = Relax(eligible, p => p.Id != state.PreviousTargetId);
        return random.PickWeighted(pool, p => 1d / (1 + p.TurnsAsTarget));
    }

    private static Player PickChallenger(GameState state, List<Player> eligible, Player target, IRandomSource random)
    {
        // Rule 1: no self-targeting. This one never bends.
        var pool = eligible.Where(p => p.Id != target.Id).ToList();

        // Rule 4: avoid the back-to-back challenger.
        pool = Relax(pool, p => p.Id != state.PreviousChallengerId);

        // Rule 3: never reproduce the exact previous pairing.
        pool = Relax(pool, p => !(target.Id == state.PreviousTargetId && p.Id == state.PreviousChallengerId));

        return random.PickWeighted(pool, p => 1d / (1 + p.TurnsAsChallenger));
    }

    /// <summary>Applies a preference, unless doing so would empty the pool.</summary>
    private static List<Player> Relax(List<Player> pool, Func<Player, bool> preference)
    {
        var filtered = pool.Where(preference).ToList();
        return filtered.Count > 0 ? filtered : pool;
    }

    /// <summary>
    /// Deals the challenger's hand. Balanced across truth/dare where the pool
    /// allows, and excluding questions the target wrote themselves — being dared
    /// by your own dare deflates the moment.
    /// </summary>
    public static IReadOnlyList<Question> DealHand(GameState state, Guid targetId, IRandomSource random)
    {
        var available = state.Questions.Where(q => !q.Used).ToList();

        if (available.Count == 0)
        {
            return [];
        }

        // Preference, not a hard rule: if excluding the target's own questions
        // would leave us short, deal them anyway rather than stalling the round.
        var preferred = available.Where(q => q.AuthorId != targetId).ToList();
        var pool = preferred.Count >= state.Settings.HandSize ? preferred : available;

        var truths = random.Shuffled(pool.Where(q => q.Kind == QuestionKind.Truth));
        var dares = random.Shuffled(pool.Where(q => q.Kind == QuestionKind.Dare));

        return Interleave(truths, dares, state.Settings.HandSize, random);
    }

    /// <summary>
    /// Alternates between the two kinds so a hand is as balanced as the pool
    /// permits, starting from whichever side is deeper. Falls through to the
    /// remaining kind once one runs out.
    /// </summary>
    private static List<Question> Interleave(
        List<Question> truths,
        List<Question> dares,
        int handSize,
        IRandomSource random)
    {
        var takeTruthFirst = truths.Count != dares.Count
            ? truths.Count > dares.Count
            : random.Next(2) == 0;

        var (first, second) = takeTruthFirst ? (truths, dares) : (dares, truths);
        var hand = new List<Question>(handSize);
        int i = 0, j = 0;

        while (hand.Count < handSize && (i < first.Count || j < second.Count))
        {
            if (i < first.Count)
            {
                hand.Add(first[i++]);
            }

            if (hand.Count < handSize && j < second.Count)
            {
                hand.Add(second[j++]);
            }
        }

        return hand;
    }
}
