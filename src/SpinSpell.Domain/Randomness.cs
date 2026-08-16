namespace SpinSpell.Domain;

/// <summary>
/// Randomness is an explicit input to the engine, never ambient state.
/// That is what makes a whole game reproducible from (initialState, commands, seed).
/// </summary>
public interface IRandomSource
{
    /// <summary>Uniform in [0, maxExclusive).</summary>
    int Next(int maxExclusive);

    /// <summary>Uniform in [0, 1).</summary>
    double NextDouble();
}

/// <summary>Deterministic default. Seed it and a game replays identically.</summary>
public sealed class SeededRandom(int seed) : IRandomSource
{
    private readonly Random _random = new(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);

    public double NextDouble() => _random.NextDouble();
}

public static class RandomExtensions
{
    /// <summary>
    /// Picks one item with probability proportional to its weight.
    /// Falls back to uniform choice if every weight is zero.
    /// </summary>
    public static T PickWeighted<T>(this IRandomSource random, IReadOnlyList<T> items, Func<T, double> weight)
    {
        ArgumentOutOfRangeException.ThrowIfZero(items.Count);

        var weights = items.Select(weight).Select(w => double.IsFinite(w) && w > 0 ? w : 0).ToArray();
        var total = weights.Sum();

        if (total <= 0)
        {
            return items[random.Next(items.Count)];
        }

        var roll = random.NextDouble() * total;
        var cumulative = 0d;

        for (var i = 0; i < items.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
            {
                return items[i];
            }
        }

        return items[^1];
    }

    /// <summary>Fisher-Yates, returning a new list.</summary>
    public static List<T> Shuffled<T>(this IRandomSource random, IEnumerable<T> source)
    {
        var list = source.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
