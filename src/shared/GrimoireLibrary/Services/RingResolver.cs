namespace Grimoire.Library.Services;

/// <summary>
/// Turns the Comfort ↔ Abyss slider into a pair of ring radii for the discovery engine
/// (DECISIONS D4, corrected by D26). The key insight of D26: a ring expressed as absolute
/// cosine radii is not interpretable, because every nomic-embed-text distance falls in a thin
/// shell — <c>BETWEEN 0.15 AND 0.35</c> would select the whole corpus at any slider position.
///
/// <para>
/// So the ring is expressed in <b>percentiles of the taste-to-artist distance distribution</b>.
/// The engine samples a few thousand artists, measures their distance to the user's taste, and
/// asks this class for the radii at the slider's two percentiles: <em>percentiles toward the
/// user, radii toward the index</em>. Those radii are then handed to the HNSW query, which
/// avoids an <c>ORDER BY</c> over the whole catalogue.
/// </para>
///
/// <para>Pure and deterministic — unit-tested without a database.</para>
/// </summary>
public static class RingResolver
{
    /// <summary>Default width of the percentile window the slider slides across the distribution.</summary>
    public const double DefaultWidthPct = 0.20;

    /// <summary>
    /// Maps the Comfort ↔ Abyss slider to a percentile window. <paramref name="comfort"/> is in
    /// [0, 1]: 0 is Comfort (nearest neighbours, the bands most like you), 1 is Abyss (the
    /// farthest bands that still fall inside your tolerance). The window of width
    /// <paramref name="widthPct"/> slides from the low end of the distribution to the high end,
    /// so the slider always selects a genuine ring, never the whole corpus.
    /// </summary>
    public static (double LoPct, double HiPct) Percentiles(double comfort, double widthPct = DefaultWidthPct)
    {
        if (widthPct is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPct), widthPct, "Window width must be in (0, 1).");
        }

        double c = Math.Clamp(comfort, 0.0, 1.0);
        double lo = c * (1.0 - widthPct);
        double hi = lo + widthPct;

        return (lo, hi);
    }

    /// <summary>
    /// Resolves the inner and outer ring radii from the slider and a sample of taste-to-artist
    /// cosine distances. Returns the two distances at the slider's percentiles.
    /// </summary>
    /// <exception cref="ArgumentException">If the sample is empty.</exception>
    public static (double RLo, double RHi) ResolveRadii(
        double comfort,
        IReadOnlyList<double> sampleDistances,
        double widthPct = DefaultWidthPct)
    {
        (double loPct, double hiPct) = Percentiles(comfort, widthPct);

        double rLo = NeighborStats.Percentile(sampleDistances, loPct);
        double rHi = NeighborStats.Percentile(sampleDistances, hiPct);

        return (rLo, rHi);
    }

    /// <summary>
    /// The safe radius around the repulsion vector (DECISIONS D4 — repulsion actively subtracts).
    /// Any candidate closer to the banished centroid than this radius is excluded. It is the
    /// distance at the <paramref name="nearPct"/> percentile of the repulsion-distance sample:
    /// the nearest <paramref name="nearPct"/> fraction of the corpus to what you banished is
    /// pushed out of the pool.
    /// </summary>
    /// <exception cref="ArgumentException">If the sample is empty.</exception>
    public static double SafeRadius(IReadOnlyList<double> repulsionDistances, double nearPct = 0.20)
    {
        return NeighborStats.Percentile(repulsionDistances, nearPct);
    }
}
