namespace Grimoire.Server.Services;

/// <summary>
/// Pure set arithmetic for comparing two bands (B24): the overlap of their tags. The vector
/// distance and the shared members come straight from pgvector and the graph, but the tag
/// comparison is worth isolating and testing — the intersection and the Jaccard index are easy
/// to get subtly wrong (case, duplicates, empty sets). Case-insensitive throughout.
/// </summary>
public static class CompareMath
{
    /// <summary>
    /// The tags common to both bands, lower-cased and de-duplicated, in a stable alphabetical order.
    /// </summary>
    public static IReadOnlyList<string> SharedTags(IEnumerable<string> a, IEnumerable<string> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        HashSet<string> left = Normalise(a);
        HashSet<string> right = Normalise(b);

        left.IntersectWith(right);

        return left.OrderBy(t => t, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Jaccard similarity of the two tag sets: |A ∩ B| / |A ∪ B|, in [0, 1]. Two bands with no
    /// tags at all return 0 (no evidence of similarity, never a divide-by-zero), which is honest:
    /// an empty tag set is the underground's default, not a claim of dissimilarity.
    /// </summary>
    public static double TagJaccard(IEnumerable<string> a, IEnumerable<string> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        HashSet<string> left = Normalise(a);
        HashSet<string> right = Normalise(b);

        if (left.Count == 0 && right.Count == 0)
        {
            return 0.0;
        }

        int intersection = left.Count(right.Contains);
        int union = left.Count + right.Count - intersection;

        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static HashSet<string> Normalise(IEnumerable<string> tags)
    {
        HashSet<string> set = new(StringComparer.Ordinal);

        foreach (string tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                set.Add(tag.Trim().ToLowerInvariant());
            }
        }

        return set;
    }
}
