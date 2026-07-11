namespace Grimoire.Server.Services;

/// <summary>
/// Parses a split release's title (C9). In the underground a split is titled by naming its bands
/// with a slash — "Xasthur / Leviathan", "Agathocles / Cripple Bastards". This isolates the pure
/// parse so it is tested directly: the band names are the slash-separated parts, trimmed. A title
/// with no slash is not a split title (empty result).
/// </summary>
public static class SplitTitle
{
    private const string Separator = " / ";

    /// <summary>
    /// The band-name parts of a split title. Returns the trimmed, non-empty segments when the title
    /// contains at least two of them, and nothing otherwise (a title with no slash is not a split).
    /// </summary>
    public static IReadOnlyList<string> Parts(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || !title.Contains(Separator, StringComparison.Ordinal))
        {
            return [];
        }

        List<string> parts = title
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return parts.Count >= 2 ? parts : [];
    }
}
