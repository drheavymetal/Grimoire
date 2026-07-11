using Grimoire.Library.Models;

namespace Grimoire.Server.Services;

/// <summary>
/// The Depth Score (feature B15, SPEC §6): it measures how <b>far</b> the user has travelled, not
/// how much they listen. It sums points over the bands the user has summoned, awarding more for
/// rarer finds — <c>Nameless &gt; Forgotten &gt; Hidden &gt; Obscure &gt; Known</c>.
///
/// <para>
/// A band whose rank is <c>null</c> (listener count unknown, so no tier) scores <b>0</b>: an absent
/// rank is never invented into points. While Last.fm is unkeyed and every rank is null the score
/// stays at 0 for everyone, degrading with dignity rather than lying (DECISIONS D31/D33).
/// </para>
///
/// <para>Pure and deterministic — unit-tested without a database.</para>
/// </summary>
public static class DepthScore
{
    /// <summary>
    /// Points a single summoned band contributes, by rarity tier. Rarer tiers are worth more.
    /// A <c>null</c> rank scores 0 — an unknown rank contributes nothing (no invented data).
    /// </summary>
    public static int Points(Rank? rank)
    {
        return rank switch
        {
            Rank.Nameless => 5,
            Rank.Forgotten => 4,
            Rank.Hidden => 3,
            Rank.Obscure => 2,
            Rank.Known => 1,
            _ => 0, // null: listeners unknown, so no rank and no points.
        };
    }

    /// <summary>The total Depth Score: the sum of <see cref="Points"/> over everything summoned.</summary>
    public static int Compute(IEnumerable<Rank?> summonedRanks)
    {
        ArgumentNullException.ThrowIfNull(summonedRanks);

        int total = 0;

        foreach (Rank? rank in summonedRanks)
        {
            total += Points(rank);
        }

        return total;
    }
}
