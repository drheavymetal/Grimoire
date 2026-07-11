namespace Grimoire.Server.Services;

/// <summary>
/// The rarity term of the discovery ordering (SPEC §6) and the weighted-random pick that applies
/// it inside the ring. Rarity is <b>inverse to popularity</b>: the fewer listeners a band has, the
/// more a discovery is worth, so the engine biases toward rarer bands — but only biases. The ring
/// (D26/D31) still owns the selection; this term reorders <em>within</em> it and never replaces the
/// random-within-ring exploration.
///
/// <para>
/// Pure and deterministic (the randomness is injected), so it is unit-tested without a database.
/// </para>
/// </summary>
public static class RaritySelector
{
    /// <summary>
    /// Default weight of the rarity term. Tunable via <see cref="RiteEngineOptions.RarityWeight"/>;
    /// 0 disables the term and the pick falls back to uniform-within-ring (the pre-D31 behaviour).
    /// </summary>
    public const double DefaultRarityWeight = 0.15;

    /// <summary>
    /// The rarity term for one band: <c>ln(1e6 / GREATEST(listeners, 1)) * weight</c> (SPEC §6).
    /// Fewer listeners → larger term → more likely to be served.
    ///
    /// <para>
    /// <b>Null handling — the crux.</b> A <c>null</c> listener count returns <b>0</b>, a NEUTRAL
    /// term, not a huge one. Most of the dark tail has no Last.fm entry, so a null is "unknown",
    /// not "infinitely rare". <c>GREATEST(NULL,1)</c> / <c>ln(NULL)</c> would make those bands win
    /// every draw — the exact opposite of intent, since it is the bands we know least about that
    /// would dominate. A neutral 0 makes an unknown band weigh the same as a band sitting right at
    /// 1e6 listeners: it competes on the random draw alone and never takes over the selection. When
    /// <c>listeners</c> is populated, the term is exactly the SPEC formula.
    /// </para>
    /// </summary>
    public static double RarityTerm(int? listeners, double weight)
    {
        if (listeners is null)
        {
            // Unknown, not rarest: a null must weigh neutrally so it cannot dominate the ring.
            return 0.0;
        }

        int count = Math.Max(listeners.Value, 1); // GREATEST(listeners, 1)
        return weight * Math.Log(1_000_000.0 / count);
    }

    /// <summary>
    /// Picks one index from the ring, weighted toward rarer bands, via Gumbel-max sampling:
    /// <c>argmax_i (rarityTerm_i + g_i)</c> with <c>g_i = -ln(-ln(u_i))</c> and each <c>u_i</c>
    /// uniform in (0, 1). This selects band <c>i</c> with probability proportional to
    /// <c>exp(rarityTerm_i)</c>, so it biases toward rarity <b>without</b> ever collapsing to
    /// "always the single rarest band" — the random-within-ring exploration survives. With every
    /// term equal (e.g. <c>weight = 0</c>, or an all-null ring) the pick is uniform, recovering the
    /// previous behaviour exactly.
    /// </summary>
    /// <param name="rarityTerms">The per-candidate rarity terms, in candidate order.</param>
    /// <param name="nextUnit">Source of uniform draws; each call should return a value in (0, 1).</param>
    /// <returns>The chosen index, or <c>-1</c> if the list is empty.</returns>
    public static int SelectIndex(IReadOnlyList<double> rarityTerms, Func<double> nextUnit)
    {
        ArgumentNullException.ThrowIfNull(rarityTerms);
        ArgumentNullException.ThrowIfNull(nextUnit);

        int bestIndex = -1;
        double bestKey = double.NegativeInfinity;

        for (int i = 0; i < rarityTerms.Count; i++)
        {
            // Keep u strictly inside (0, 1): ln(0) and ln(1) blow the Gumbel transform up.
            double u = Math.Clamp(nextUnit(), double.Epsilon, 1.0 - 1e-12);
            double gumbel = -Math.Log(-Math.Log(u));
            double key = rarityTerms[i] + gumbel;

            if (key > bestKey)
            {
                bestKey = key;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
