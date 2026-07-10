namespace Grimoire.Library.Services;

/// <summary>
/// Taste- and repulsion-vector arithmetic for The Rite (DECISIONS D4, D15, D26).
///
/// <para>
/// CRITICAL INVARIANT — the double-centring trap (CLAUDE.md, D26). Every embedding this
/// class touches is <b>already centred</b>: the ETL subtracted the corpus mean before
/// indexing (variant C). A taste vector built by averaging stored artist embeddings is
/// therefore already in centred space. Do <b>not</b> subtract the corpus mean again here —
/// that would be a second subtraction and the ring search would break. The mean persisted
/// in <c>corpus_stats</c> exists only to centre an <em>external raw</em> query vector (e.g.
/// a fresh Ollama embedding), never a taste assembled from vectors already in the table.
/// </para>
///
/// <para>
/// The taste (summoned) and repulsion (banished) vectors are exponential moving averages,
/// which is what "media con decay" means: each resolution nudges the vector toward the
/// artist just judged, so recent judgements weigh more than old ones. Pure and deterministic
/// so it is unit-tested without a database.
/// </para>
/// </summary>
public static class TasteMath
{
    /// <summary>How strongly each summon/banish pulls the vector toward the judged artist.</summary>
    public const double DefaultDecay = 0.25;

    /// <summary>
    /// Cold-start seed (DECISIONS D15): the mean of the chosen artists' already-centred
    /// embeddings. No re-centring — the inputs are centred, so the mean is centred too.
    /// </summary>
    /// <exception cref="ArgumentException">If the set is empty or dimensions differ.</exception>
    public static float[] Seed(IReadOnlyList<float[]> centredEmbeddings)
    {
        return VectorMath.Mean(centredEmbeddings);
    }

    /// <summary>
    /// Summon: pull the taste vector toward the summoned artist by <paramref name="decay"/>.
    /// A null current taste means the artist becomes the taste (first summon without a seed).
    /// </summary>
    public static float[] ApplySummon(float[]? taste, float[] artist, double decay = DefaultDecay)
    {
        ArgumentNullException.ThrowIfNull(artist);

        if (taste is null)
        {
            return (float[])artist.Clone();
        }

        return Blend(taste, artist, decay);
    }

    /// <summary>
    /// Banish: the repulsion vector is the decayed running mean of banished artists (DECISIONS
    /// D4 — "a recommender that learns from the no's is rare, and it shows"). A null current
    /// repulsion means the banished artist becomes the repulsion seed.
    /// </summary>
    public static float[] ApplyBanish(float[]? repulsion, float[] artist, double decay = DefaultDecay)
    {
        ArgumentNullException.ThrowIfNull(artist);

        if (repulsion is null)
        {
            return (float[])artist.Clone();
        }

        return Blend(repulsion, artist, decay);
    }

    /// <summary>Exponential moving average: <c>(1 − w)·current + w·incoming</c>, element-wise.</summary>
    private static float[] Blend(float[] current, float[] incoming, double weight)
    {
        if (current.Length != incoming.Length)
        {
            throw new ArgumentException("Taste and artist vectors must share the same dimension.");
        }

        if (weight is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(weight), weight, "Decay must be in (0, 1].");
        }

        float[] result = new float[current.Length];

        for (int i = 0; i < current.Length; i++)
        {
            result[i] = (float)(((1.0 - weight) * current[i]) + (weight * incoming[i]));
        }

        return result;
    }
}
