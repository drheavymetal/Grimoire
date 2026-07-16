using System.Text.Json;
using Grimoire.Library.Wikidata;

namespace Grimoire.Library.Services;

/// <summary>
/// The biography Grimoire keeps for an artist: the plain-text extract and the canonical URL of the
/// source Wikipedia article (kept for the CC BY-SA attribution the licence requires).
/// </summary>
public sealed record WikipediaBiography(string Abstract, string Url);

/// <summary>
/// Pure parsing for the Wikipedia biography pass, kept out of the HTTP layer so it can be tested
/// without a network (mirrors <see cref="MetalArchivesParser"/> / <see cref="LastFmListeners"/>).
/// Two steps, both side-effect free: read the English-Wikipedia article title out of a Wikidata
/// SPARQL result (the match is by MusicBrainz id via <c>wdt:P434</c>, so it is exact — no homonym
/// guessing), then read the extract and canonical URL out of the Wikipedia REST summary response.
/// Anything missing or malformed yields <c>null</c>: a gap, never an invented biography (Invariant 5).
/// </summary>
public static class WikipediaSummary
{
    /// <summary>
    /// Pulls the English-Wikipedia article title out of a SPARQL result whose <c>?article</c>
    /// variable binds the article URL (e.g. <c>https://en.wikipedia.org/wiki/Darkthrone</c>). The
    /// title is the path segment after <c>/wiki/</c>, left percent-encoded and with underscores
    /// intact so it can be handed straight to the REST summary endpoint. Returns <c>null</c> when
    /// the response has no usable binding.
    /// </summary>
    public static string? ParseArticleTitle(SparqlResponse? response, string articleVar = "article")
    {
        if (response?.Results?.Bindings is null)
        {
            return null;
        }

        foreach (Dictionary<string, SparqlValue> row in response.Results.Bindings)
        {
            if (!row.TryGetValue(articleVar, out SparqlValue? value) || string.IsNullOrWhiteSpace(value.Value))
            {
                continue;
            }

            const string Marker = "/wiki/";
            int at = value.Value.IndexOf(Marker, StringComparison.Ordinal);

            if (at < 0)
            {
                continue;
            }

            string title = value.Value[(at + Marker.Length)..];

            if (title.Length > 0)
            {
                return title;
            }
        }

        return null;
    }

    /// <summary>
    /// Batch counterpart of <see cref="ParseArticleTitle"/>: reads a SPARQL result that binds both
    /// <c>?mbid</c> (the MusicBrainz id literal it was asked about) and <c>?article</c> (the enwiki
    /// article URL) into a map from lower-case MBID to article title. One WDQS round trip resolves a
    /// whole batch this way instead of one query per artist. Rows without a usable MBID or article
    /// binding are skipped; a duplicate MBID keeps the first title seen. Returns an empty map when the
    /// response has no bindings (a definitive "none of these have an article", never an error).
    /// </summary>
    public static Dictionary<string, string> ParseArticleTitles(
        SparqlResponse? response, string mbidVar = "mbid", string articleVar = "article")
    {
        Dictionary<string, string> titles = new(StringComparer.OrdinalIgnoreCase);

        if (response?.Results?.Bindings is null)
        {
            return titles;
        }

        foreach (Dictionary<string, SparqlValue> row in response.Results.Bindings)
        {
            if (!row.TryGetValue(mbidVar, out SparqlValue? mbid) || string.IsNullOrWhiteSpace(mbid.Value))
            {
                continue;
            }

            if (!row.TryGetValue(articleVar, out SparqlValue? article) || string.IsNullOrWhiteSpace(article.Value))
            {
                continue;
            }

            const string Marker = "/wiki/";
            int at = article.Value.IndexOf(Marker, StringComparison.Ordinal);

            if (at < 0)
            {
                continue;
            }

            string title = article.Value[(at + Marker.Length)..];

            if (title.Length > 0)
            {
                titles.TryAdd(mbid.Value, title);
            }
        }

        return titles;
    }

    /// <summary>
    /// The REST summary path for one article title, with the title escaped into a <b>single</b> path
    /// segment. Escaping is not cosmetic: Wikipedia titles may contain slashes ("Fliflet/Hamre",
    /// "The Yes/No People"), and interpolated raw those become extra path segments that the REST API
    /// rejects with 400 — the artist is never resolved, and if the caller also mistakes that 400 for
    /// a transient failure it retries the same broken URL for ever (MEMORY §6f).
    /// </summary>
    public static string SummaryPath(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        return $"api/rest_v1/page/summary/{Uri.EscapeDataString(title)}";
    }

    /// <summary>
    /// Reads the extract and canonical desktop URL out of a Wikipedia REST summary response
    /// (<c>api/rest_v1/page/summary/{title}</c>). Returns <c>null</c> when the JSON is malformed, or
    /// the <c>extract</c> is missing or blank — a missing biography is a gap, not an error. The URL
    /// falls back to the article title when the response omits <c>content_urls.desktop.page</c>.
    /// </summary>
    public static WikipediaBiography? ParseSummary(string? json, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("extract", out JsonElement extractElement)
                || extractElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string extract = extractElement.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(extract))
            {
                return null;
            }

            string? url = ReadDesktopPage(root)
                ?? (title is not null ? $"https://en.wikipedia.org/wiki/{title}" : null);

            if (url is null)
            {
                return null;
            }

            return new WikipediaBiography(extract.Trim(), url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadDesktopPage(JsonElement root)
    {
        if (root.TryGetProperty("content_urls", out JsonElement contentUrls)
            && contentUrls.ValueKind == JsonValueKind.Object
            && contentUrls.TryGetProperty("desktop", out JsonElement desktop)
            && desktop.ValueKind == JsonValueKind.Object
            && desktop.TryGetProperty("page", out JsonElement page)
            && page.ValueKind == JsonValueKind.String)
        {
            string? value = page.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}
