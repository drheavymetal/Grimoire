namespace Grimoire.Library.Services;

/// <summary>
/// Drift arithmetic for the taste trajectory (feature C16). Given the ordered history of a user's
/// taste vectors, the drift at step <c>i</c> is the cosine distance between snapshot <c>i</c> and
/// <c>i − 1</c> — how far the taste moved with that summon. Pure and deterministic; the vectors are
/// already centred (DECISIONS D26) so the distances are directly comparable, and nothing re-centres.
/// </summary>
public static class TrajectoryMath
{
    /// <summary>
    /// The per-step drift series for a taste history in chronological order. Returns an array of
    /// length <c>max(0, n − 1)</c>: element <c>i</c> is the cosine distance from snapshot <c>i</c>
    /// to snapshot <c>i + 1</c>. A single snapshot (or none) has no drift, so the series is empty.
    /// </summary>
    public static double[] DriftSeries(IReadOnlyList<float[]> orderedEmbeddings)
    {
        ArgumentNullException.ThrowIfNull(orderedEmbeddings);

        if (orderedEmbeddings.Count < 2)
        {
            return [];
        }

        double[] series = new double[orderedEmbeddings.Count - 1];

        for (int i = 1; i < orderedEmbeddings.Count; i++)
        {
            series[i - 1] = VectorMath.CosineDistance(orderedEmbeddings[i - 1], orderedEmbeddings[i]);
        }

        return series;
    }

    /// <summary>
    /// Total drift travelled: the cosine distance between the first and last snapshot. Zero when
    /// there are fewer than two snapshots. This is the "how far the taste has moved from where it
    /// started", distinct from the sum of steps (which measures the wandering path length).
    /// </summary>
    public static double TotalDrift(IReadOnlyList<float[]> orderedEmbeddings)
    {
        ArgumentNullException.ThrowIfNull(orderedEmbeddings);

        if (orderedEmbeddings.Count < 2)
        {
            return 0.0;
        }

        return VectorMath.CosineDistance(orderedEmbeddings[0], orderedEmbeddings[^1]);
    }
}
