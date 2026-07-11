namespace Grimoire.Library.Services;

/// <summary>
/// The Dark Twin (feature B18): the user whose taste is closest to yours but whose collection is the
/// most disjoint — someone who likes what you like yet has heard almost none of the same bands.
/// Pure and deterministic so the ranking is unit-tested without a database.
///
/// <para>
/// The score rewards both halves at once: <c>tasteSimilarity × disjointness</c>, where
/// <c>tasteSimilarity = 1 − cosineDistance(taste, taste)</c> and <c>disjointness = 1 − Jaccard</c>
/// of the two summoned-band sets. A near-taste twin who shares your whole grimoire scores low (no
/// discovery to offer); a disjoint stranger with alien taste also scores low. The interesting twin
/// maximises the product. Ties break on the smaller user id for determinism.
/// </para>
/// </summary>
public static class DarkTwinMath
{
    /// <summary>A candidate other user: their taste vector and the set of bands they have summoned.</summary>
    public readonly record struct Candidate(Guid UserId, float[] Taste, IReadOnlySet<Guid> Summoned);

    /// <summary>The chosen twin and the two numbers that justify the choice.</summary>
    public readonly record struct TwinResult(Guid UserId, double TasteSimilarity, double Disjointness, double Score);

    /// <summary>
    /// Picks the best Dark Twin for a user, or null when there is no eligible candidate (too few
    /// users — the honest empty state of B18). A candidate is eligible only if the union of the two
    /// grimoires is non-empty, so disjointness is defined.
    /// </summary>
    public static TwinResult? Best(
        float[] myTaste,
        IReadOnlySet<Guid> mySummoned,
        IReadOnlyList<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(myTaste);
        ArgumentNullException.ThrowIfNull(mySummoned);
        ArgumentNullException.ThrowIfNull(candidates);

        TwinResult? best = null;

        foreach (Candidate c in candidates)
        {
            if (c.Summoned.Count == 0)
            {
                // A twin with an empty collection has nothing to offer, and its disjointness would be
                // a trivial 1.0 — it must never win. The Dark Twin is "what THEY have that you lack".
                continue;
            }

            int intersection = mySummoned.Count(c.Summoned.Contains);
            int union = mySummoned.Count + c.Summoned.Count - intersection;

            if (union == 0)
            {
                // Unreachable given the guard above, but kept: disjointness is undefined at union 0.
                continue;
            }

            double disjointness = 1.0 - ((double)intersection / union);
            double similarity = 1.0 - VectorMath.CosineDistance(myTaste, c.Taste);
            double score = similarity * disjointness;

            if (best is null
                || score > best.Value.Score
                || (score == best.Value.Score && c.UserId.CompareTo(best.Value.UserId) < 0))
            {
                best = new TwinResult(c.UserId, similarity, disjointness, score);
            }
        }

        return best;
    }
}
