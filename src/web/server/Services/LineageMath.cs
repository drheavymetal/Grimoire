namespace Grimoire.Server.Services;

/// <summary>
/// Small pure helpers for the lineage features that are not graph traversals: the diaspora
/// "went after" test (B11) and the missing-link midpoint (C5). Kept free of the database so both
/// can be unit-tested directly.
/// </summary>
public static class LineageMath
{
    /// <summary>
    /// Whether a musician joined a destination band <b>after</b> leaving the source band — the test
    /// behind the diaspora (B11): a band breaks up, and we follow each departing member to where they
    /// went next.
    ///
    /// <para>
    /// A destination counts only when both dates are known and the destination began on or after the
    /// departure. A null departure (still a member — never left) or a null destination start
    /// (unknown when they joined) yields <c>false</c>: we do not assert a move we cannot date, rather
    /// than invent an order (R2, honest degradation).
    /// </para>
    /// </summary>
    public static bool WentAfterLeaving(DateOnly? departure, DateOnly? destinationBegin)
    {
        if (departure is not DateOnly left || destinationBegin is not DateOnly joined)
        {
            return false;
        }

        return joined >= left;
    }

    /// <summary>
    /// The midpoint of two embeddings — the interpolation behind the missing link (C5): "what lives
    /// <i>between</i> these two bands?". The embeddings are already centred (DECISIONS D26/D31), so
    /// the average is centred too and directly comparable against the indexed vectors; the corpus
    /// mean is never subtracted again here.
    /// </summary>
    /// <exception cref="ArgumentException">If the vectors differ in length or are empty.</exception>
    public static float[] Midpoint(float[] a, float[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Length != b.Length)
        {
            throw new ArgumentException("Both embeddings must share the same dimension.");
        }

        if (a.Length == 0)
        {
            throw new ArgumentException("Cannot interpolate empty vectors.", nameof(a));
        }

        float[] mid = new float[a.Length];

        for (int i = 0; i < a.Length; i++)
        {
            mid[i] = (a[i] + b[i]) / 2f;
        }

        return mid;
    }
}
