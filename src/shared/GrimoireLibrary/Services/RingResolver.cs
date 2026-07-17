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
    /// Exponent that bends the slider's travel toward the low percentiles (DECISIONS D68).
    /// <para>
    /// A linear slider (curve 1.0) is a trap that percentiles set for us: the median distance to
    /// your taste <b>is</b> the median distance of the corpus, by definition, so a slider at its
    /// midpoint necessarily serves the typical band — which is a band drawn at random. Measured on
    /// production (2026-07-17): at comfort 0.5 the linear map served bands matching the listener's
    /// genres 52.7% of the time against a coin-flip baseline of 50.5%, and the whole upper half of
    /// the travel sat at or below chance. The engine was doing nothing the whole way.
    /// </para>
    /// <para>
    /// The signal is not spread evenly across the percentiles — it is packed into the bottom fifth
    /// (measured: 95.7% on-target below the 20th percentile, 35% by the 60th). Squaring the slider
    /// spends most of its travel where the signal actually lives, without capping the reach: the
    /// abyss is still reachable at 1.0. This is calibration, not a new mechanism — the percentile
    /// ring of D26 is intact and still adapts per listener.
    /// </para>
    /// </summary>
    public const double DefaultReachCurve = 2.0;

    /// <summary>
    /// Maps the Comfort ↔ Abyss slider to a percentile window. <paramref name="comfort"/> is in
    /// [0, 1]: 0 is Comfort (nearest neighbours, the bands most like you), 1 is Abyss (the
    /// farthest bands that still fall inside your tolerance). The window of width
    /// <paramref name="widthPct"/> slides from the low end of the distribution to the high end,
    /// so the slider always selects a genuine ring, never the whole corpus.
    /// <para>
    /// The travel is bent by <paramref name="reachCurve"/> (see <see cref="DefaultReachCurve"/>):
    /// the window's position is <c>comfort^curve</c>, not <c>comfort</c>. Both ends are fixed
    /// points — comfort 0 and comfort 1 land exactly where they did — so only the distribution of
    /// the middle changes.
    /// </para>
    /// </summary>
    public static (double LoPct, double HiPct) Percentiles(
        double comfort,
        double widthPct = DefaultWidthPct,
        double reachCurve = DefaultReachCurve)
    {
        if (widthPct is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPct), widthPct, "Window width must be in (0, 1).");
        }

        if (reachCurve <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reachCurve), reachCurve, "Reach curve must be positive.");
        }

        double c = Math.Clamp(comfort, 0.0, 1.0);
        double lo = Math.Pow(c, reachCurve) * (1.0 - widthPct);
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
        double widthPct = DefaultWidthPct,
        double reachCurve = DefaultReachCurve)
    {
        (double loPct, double hiPct) = Percentiles(comfort, widthPct, reachCurve);

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
