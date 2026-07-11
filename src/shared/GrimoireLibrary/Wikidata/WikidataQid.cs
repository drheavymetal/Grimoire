using System.Text.RegularExpressions;

namespace Grimoire.Library.Wikidata;

/// <summary>
/// Extracts a Wikidata item id (a QID, e.g. <c>Q220938</c>) from a URI. Handles both forms the
/// codebase sees: the human page URL stored in <c>artists.links['wikidata']</c>
/// (<c>https://www.wikidata.org/wiki/Q220938</c>) and the entity URI SPARQL returns
/// (<c>http://www.wikidata.org/entity/Q220938</c>). Pure and side-effect free so it can be
/// tested without a network.
/// </summary>
public static partial class WikidataQid
{
    [GeneratedRegex(@"^Q[1-9][0-9]*$")]
    private static partial Regex QidPattern();

    /// <summary>
    /// Returns the QID that is the last path segment of <paramref name="uri"/>, or <c>null</c>
    /// when the string is empty or does not end in a well-formed QID. Never guesses.
    /// </summary>
    public static string? FromUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        string trimmed = uri.Trim();

        // Drop any fragment or query so "…/Q123#foo" and "…/Q123?x=y" still resolve.
        int cut = trimmed.IndexOfAny(['#', '?']);

        if (cut >= 0)
        {
            trimmed = trimmed[..cut];
        }

        int slash = trimmed.LastIndexOf('/');
        string tail = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;

        return QidPattern().IsMatch(tail) ? tail : null;
    }

    /// <summary>The SPARQL prefixed form of a QID, e.g. <c>wd:Q220938</c>, for a VALUES clause.</summary>
    public static string ToPrefixed(string qid)
    {
        return $"wd:{qid}";
    }
}
