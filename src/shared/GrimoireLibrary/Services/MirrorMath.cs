namespace Grimoire.Library.Services;

/// <summary>
/// The mirror (feature C20): the app proving its own thesis with the user's own ear as witness.
/// It needs no new data — only the rite history. It finds the user's favourite genre (the tag most
/// common among the bands they summoned) and measures what fraction of the bands they <em>banished
/// blind</em> carry that same tag. "62% of the bands you rejected blind belong to your favourite
/// genre." Pure and deterministic — unit-tested without a database.
/// </summary>
public static class MirrorMath
{
    /// <summary>
    /// The outcome of the mirror. <see cref="HasData"/> is false when there is nothing to reflect
    /// (no summoned tags to name a favourite, or nothing banished to measure against) — the caller
    /// then shows a designed empty state rather than a fabricated percentage.
    /// </summary>
    public readonly record struct MirrorResult(
        bool HasData,
        string? FavouriteTag,
        int BanishedTotal,
        int BanishedMatching,
        double Fraction);

    /// <summary>
    /// Computes the mirror from the tag sets of summoned and banished bands. The favourite tag is
    /// the most frequent across the summoned bands (ties broken alphabetically, so the result is
    /// stable); the fraction is how many banished bands carry it, over all banished bands.
    /// </summary>
    public static MirrorResult Compute(
        IReadOnlyList<IReadOnlyList<string>> summonedTags,
        IReadOnlyList<IReadOnlyList<string>> banishedTags)
    {
        ArgumentNullException.ThrowIfNull(summonedTags);
        ArgumentNullException.ThrowIfNull(banishedTags);

        string? favourite = FavouriteTag(summonedTags);

        if (favourite is null || banishedTags.Count == 0)
        {
            return new MirrorResult(false, favourite, banishedTags.Count, 0, 0.0);
        }

        int matching = banishedTags.Count(tags =>
            tags.Any(t => string.Equals(t, favourite, StringComparison.OrdinalIgnoreCase)));

        double fraction = (double)matching / banishedTags.Count;

        return new MirrorResult(true, favourite, banishedTags.Count, matching, fraction);
    }

    /// <summary>The most frequent tag across the summoned bands, or null when there are none.</summary>
    private static string? FavouriteTag(IReadOnlyList<IReadOnlyList<string>> summonedTags)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);

        foreach (IReadOnlyList<string> tags in summonedTags)
        {
            // Count each distinct tag once per band, so a band that repeats a tag does not stuff it.
            foreach (string tag in tags.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }

        if (counts.Count == 0)
        {
            return null;
        }

        // Most frequent first; ties broken alphabetically for a deterministic favourite.
        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .First()
            .Key;
    }
}
