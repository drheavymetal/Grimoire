namespace Grimoire.Library.Services;

/// <summary>
/// The broad family a band belongs to, inferred from its tags. Coarse on purpose: this exists
/// only to spread the cold-start grid across the catalogue's shapes, never to label a band.
/// </summary>
public enum SeedFamily
{
    Metal,
    Rock,
    Punk,
    Folk,
    Electronic,

    /// <summary>Everything the catalogue swept in that is none of the above (pop, hip hop…).</summary>
    Other,
}

/// <summary>
/// Shaping of the cold-start "choose five" grid (DECISIONS D15).
///
/// <para>
/// Two pure pieces, both database-free so they are unit-tested without one:
/// </para>
/// <list type="bullet">
/// <item><see cref="FamilyOf"/> — the coarse family of a band, from its tags.</item>
/// <item><see cref="Interleave{T}"/> — a fair round-robin merge of several ordered lanes.</item>
/// </list>
///
/// <para>
/// Why this exists: ordering the grid by how prolific a band is buries the underground. A handful
/// of canonical acts have thousands of releases, so a "most releases first" grid is a wall of the
/// famous and an underground listener finds nothing to click. The grid is instead drawn from the
/// most-listened bands of each family in turn, so every family a user might arrive with is on
/// screen from the start.
/// </para>
/// </summary>
public static class SeedPool
{
    /// <summary>The families the starter grid draws from, in lane order. <c>Other</c> is not one of them.</summary>
    public static readonly IReadOnlyList<SeedFamily> StarterFamilies =
    [
        SeedFamily.Metal,
        SeedFamily.Rock,
        SeedFamily.Punk,
        SeedFamily.Folk,
        SeedFamily.Electronic,
    ];

    // Within ONE tag, matched in this order, so a compound lands where a listener would put it:
    // "folk metal" and "industrial metal" are metal, "punk rock" is punk, "folk rock" is folk.
    private static readonly (SeedFamily Family, string[] Needles)[] Rules =
    [
        (SeedFamily.Metal, ["metal", "nwobhm", "thrash", "doom", "sludge", "grindcore", "grind", "djent"]),
        (SeedFamily.Punk, ["punk", "hardcore", "crust", "streetpunk", "oi!"]),
        (SeedFamily.Folk, ["folk", "traditional", "celtic", "nordic", "medieval", "singer-songwriter"]),
        (SeedFamily.Electronic, ["electronic", "ambient", "industrial", "synth", "techno", "house", "drone", "noise", "darkwave", "ebm"]),
        (SeedFamily.Rock, ["rock", "grunge", "psychedelic", "progressive", "blues", "shoegaze"]),
    ];

    /// <summary>
    /// The family a band's tags put it in. No tags, or nothing recognised, is <see cref="SeedFamily.Other"/> —
    /// an honest "we don't know", never a guess dressed as a genre.
    ///
    /// <para>
    /// The tags are walked <b>in order</b> and the first one that names a family wins, because
    /// MusicBrainz orders tags by how many people voted for them: the first tag is what the band
    /// mostly is. Scanning family-first instead lets one stray tag deep in the list capture the band —
    /// the Red Hot Chili Peppers carry a buried "funk metal" and would take a metal slot on the
    /// cold-start grid, ahead of actual metal bands. That is the exact bug this ordering prevents.
    /// </para>
    /// </summary>
    public static SeedFamily FamilyOf(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return SeedFamily.Other;
        }

        foreach (string tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            string lowered = tag.ToLowerInvariant();

            foreach ((SeedFamily family, string[] needles) in Rules)
            {
                foreach (string needle in needles)
                {
                    if (lowered.Contains(needle, StringComparison.Ordinal))
                    {
                        return family;
                    }
                }
            }
        }

        return SeedFamily.Other;
    }

    /// <summary>
    /// Merges ordered lanes round-robin — one from each lane, then round again — until <paramref name="take"/>
    /// items are gathered or every lane is spent. Duplicates (the same band reachable from two lanes) are
    /// dropped by <paramref name="key"/>, keeping the first occurrence. A lane that runs dry does not steal
    /// the turn of the others: the remaining lanes simply keep going, so a thin family costs nobody its share.
    /// </summary>
    public static List<T> Interleave<T>(IReadOnlyList<IReadOnlyList<T>> lanes, int take, Func<T, object> key)
    {
        List<T> merged = [];
        HashSet<object> seen = [];

        if (lanes.Count == 0 || take <= 0)
        {
            return merged;
        }

        int depth = lanes.Max(lane => lane.Count);

        for (int round = 0; round < depth && merged.Count < take; round++)
        {
            foreach (IReadOnlyList<T> lane in lanes)
            {
                if (merged.Count >= take)
                {
                    break;
                }

                if (round >= lane.Count)
                {
                    continue;
                }

                T item = lane[round];

                if (seen.Add(key(item)))
                {
                    merged.Add(item);
                }
            }
        }

        return merged;
    }
}
