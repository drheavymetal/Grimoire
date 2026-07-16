using System.Text.Json;
using Grimoire.Library.Wikidata;

namespace Grimoire.Library.Services;

/// <summary>
/// The biography Grimoire keeps for an artist in one language: the plain-text extract and the
/// canonical URL of the source Wikipedia article (kept for the CC BY-SA attribution the licence
/// requires — the link must point at the article the text actually came from, which is why the URL
/// travels with the text instead of being rebuilt from a title later).
/// </summary>
public sealed record WikipediaBiography(string Abstract, string Url);

/// <summary>
/// Pure parsing for the Wikipedia biography pass, kept out of the HTTP layer so it can be tested
/// without a network (mirrors <see cref="MetalArchivesParser"/> / <see cref="LastFmListeners"/>).
/// Two steps, both side-effect free: read the article titles out of a Wikidata SPARQL result (the
/// match is by MusicBrainz id via <c>wdt:P434</c>, so it is exact — no homonym guessing), then read
/// the extract and canonical URL out of the Wikipedia REST summary response. Anything missing or
/// malformed yields <c>null</c>: a gap, never an invented biography (Invariant 5).
/// <para>
/// Nothing here names a language. Which editions to ask for is the caller's policy
/// (<c>Wikipedia:Languages</c>); this file only routes whatever comes back, reading each article's
/// language off its own host. That is what makes adding <c>no</c>/<c>sv</c>/<c>fi</c> configuration
/// instead of code.
/// </para>
/// </summary>
public static class WikipediaSummary
{
    private const string WikipediaHostSuffix = ".wikipedia.org";
    private const string ArticleMarker = "/wiki/";

    /// <summary>
    /// The site a Wikipedia edition is served from, as Wikidata's <c>schema:isPartOf</c> names it and
    /// as its REST API is reached: "es" → <c>https://es.wikipedia.org/</c>. One function for both
    /// because they are the same URL — which is precisely why <see cref="LanguageOf"/> can recover an
    /// article's edition from the article's own address, with nothing else to keep in sync.
    /// </summary>
    public static string SiteUrl(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        return $"https://{language}{WikipediaHostSuffix}/";
    }

    /// <summary>
    /// The edition an article URL belongs to, as a bare language code:
    /// <c>https://no.wikipedia.org/wiki/Darkthrone</c> → <c>"no"</c>. Returns null when the URL is
    /// not an article address of the shape WDQS returns — including the mobile hosts
    /// (<c>en.m.wikipedia.org</c>), whose extra label would otherwise be read as part of the language.
    /// </summary>
    public static string? LanguageOf(string? articleUrl)
    {
        if (string.IsNullOrWhiteSpace(articleUrl)
            || !Uri.TryCreate(articleUrl, UriKind.Absolute, out Uri? uri)
            || !uri.Host.EndsWith(WikipediaHostSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string language = uri.Host[..^WikipediaHostSuffix.Length];

        // A single label, and a real one: "" (wikipedia.org itself) and "en.m" are not editions.
        if (language.Length == 0 || language.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        return language.ToLowerInvariant();
    }

    /// <summary>
    /// Reads a batched SPARQL result — rows binding <c>?mbid</c> (the MusicBrainz id literal the
    /// query asked about) and <c>?article</c> (a Wikipedia article URL) — into a map from MBID to
    /// that artist's article titles keyed by language code. One WDQS round trip resolves a whole
    /// batch in every requested edition at once, so rows for different artists and different
    /// languages arrive interleaved and in no promised order; each row is routed by its own article
    /// URL, so nothing depends on the order WDQS chose.
    /// <para>
    /// Titles are left exactly as the URL carries them (underscores intact, non-ASCII raw) so
    /// <see cref="SummaryPath"/> can escape them once and correctly. Rows without a usable MBID, a
    /// usable article, or a recognisable edition are skipped; a duplicate (MBID, language) keeps the
    /// first title seen. An empty map means WDQS answered "none of these have an article" — a
    /// definitive gap, never an error, which is the caller's cue that stamping is safe.
    /// </para>
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> ParseArticleTitles(
        SparqlResponse? response, string mbidVar = "mbid", string articleVar = "article")
    {
        Dictionary<string, Dictionary<string, string>> byMbid = new(StringComparer.OrdinalIgnoreCase);

        if (response?.Results?.Bindings is null)
        {
            return byMbid;
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

            string? language = LanguageOf(article.Value);

            if (language is null)
            {
                continue;
            }

            string? title = TitleOf(article.Value);

            if (title is null)
            {
                continue;
            }

            if (!byMbid.TryGetValue(mbid.Value, out Dictionary<string, string>? byLanguage))
            {
                byLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                byMbid[mbid.Value] = byLanguage;
            }

            byLanguage.TryAdd(language, title);
        }

        return byMbid;
    }

    /// <summary>
    /// The REST summary path for one article title, with the title normalised and escaped into a
    /// <b>single</b> path segment. The path is relative: which edition it is fetched from is the
    /// client's base address, not this string's.
    /// <para>
    /// <b>Unescape, then escape</b> — and the first half is not redundant. Titles arrive as the path
    /// of a URL WDQS returned, and MediaWiki's canonical URLs are <em>inconsistently</em> encoded:
    /// <c>AC/DC</c> comes back with the slash raw, accents come back raw
    /// (<c>Héroes_del_Silencio</c>), but an ampersand comes back already escaped
    /// (<c>Bob_Marley_%26_The_Wailers</c>). Escaping such a title again turns <c>%26</c> into
    /// <c>%2526</c>, Wikipedia answers 404, and — because a 404 is a legitimate "no such article" —
    /// the band is stamped as having no biography, permanently, on the strength of a URL we
    /// corrupted ourselves. Decoding first puts every title in one known form so it can be encoded
    /// exactly once.
    /// </para>
    /// <para>
    /// Escaping at all is likewise not cosmetic: titles may contain slashes ("Fliflet/Hamre", "The
    /// Yes/No People"), and interpolated raw those become extra path segments the REST API rejects
    /// with 400 — the artist is never resolved, and if the caller also mistakes that 400 for a
    /// transient failure it retries the same broken URL for ever (MEMORY §6f). Round-tripping is
    /// safe for a literal percent, too: MediaWiki writes that as <c>%25</c>, so decoding is the exact
    /// inverse of what produced the URL.
    /// </para>
    /// </summary>
    public static string SummaryPath(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        return $"api/rest_v1/page/summary/{Uri.EscapeDataString(Uri.UnescapeDataString(title))}";
    }

    /// <summary>
    /// Reads the extract and canonical desktop URL out of a Wikipedia REST summary response
    /// (<c>api/rest_v1/page/summary/{title}</c>). Returns <c>null</c> when the JSON is malformed, or
    /// the <c>extract</c> is missing or blank — a missing biography is a gap, not an error. The URL
    /// falls back to the article's address in <paramref name="language"/>'s edition when the response
    /// omits <c>content_urls.desktop.page</c>; that fallback must never assume English, or a
    /// Norwegian biography would be credited to an enwiki article that does not exist and the CC BY-SA
    /// attribution would point at the wrong text.
    /// </summary>
    public static WikipediaBiography? ParseSummary(string? json, string? title, string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

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
                ?? (title is not null ? $"{SiteUrl(language)}wiki/{title}" : null);

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

    private static string? TitleOf(string articleUrl)
    {
        int at = articleUrl.IndexOf(ArticleMarker, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        string title = articleUrl[(at + ArticleMarker.Length)..];

        return title.Length > 0 ? title : null;
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
