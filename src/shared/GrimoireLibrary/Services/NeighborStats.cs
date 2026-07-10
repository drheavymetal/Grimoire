namespace Grimoire.Library.Services;

/// <summary>
/// Percentile statistics over neighbour distances, used by the <c>stats</c> command to
/// prove the centred embeddings (DECISIONS D26) actually spread the catalogue. The whole
/// point: if the p10, p50 and p90 neighbour distances come out nearly equal, the space is
/// still the thin shell that D26 set out to fix and the Comfort ↔ Abyss slider would move
/// nothing — the engine is broken at this scale. The numbers must diverge.
/// </summary>
public static class NeighborStats
{
    /// <summary>
    /// Linear-interpolation percentile (the "R-7" / Excel definition) over a sample.
    /// <paramref name="p"/> is in [0, 1]. The input need not be sorted.
    /// </summary>
    /// <exception cref="ArgumentException">If the sample is empty.</exception>
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            throw new ArgumentException("Cannot take a percentile of an empty sample.", nameof(values));
        }

        if (p is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(p), p, "Percentile must be in [0, 1].");
        }

        double[] sorted = values.ToArray();
        Array.Sort(sorted);

        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        double rank = p * (sorted.Length - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        double frac = rank - lo;

        return sorted[lo] + (frac * (sorted[hi] - sorted[lo]));
    }

    /// <summary>The three neighbour-distance percentiles the stats command reports.</summary>
    public readonly record struct Spread(double P10, double P50, double P90)
    {
        /// <summary>
        /// True when the three percentiles are effectively identical — the failure mode D26
        /// exists to prevent. <paramref name="epsilon"/> is the absolute gap below which the
        /// shell is considered degenerate.
        /// </summary>
        public bool IsDegenerate(double epsilon = 0.01)
        {
            return (P90 - P10) < epsilon;
        }
    }

    /// <summary>
    /// Given the distances from one artist to its neighbours, returns the 10th/50th/90th
    /// percentile distances. This is the per-artist shape the slider must be able to traverse.
    /// </summary>
    public static Spread SpreadOf(IReadOnlyList<double> neighborDistances)
    {
        return new Spread(
            Percentile(neighborDistances, 0.10),
            Percentile(neighborDistances, 0.50),
            Percentile(neighborDistances, 0.90));
    }
}
