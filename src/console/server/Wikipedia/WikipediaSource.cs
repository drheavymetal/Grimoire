using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Library.Wikidata;
using Grimoire.Worker.Preview;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Wikipedia;

/// <summary>
/// Resolves one artist's English-Wikipedia biography, matched <b>only</b> by MusicBrainz id so a
/// homonym can never be mistaken for our band (the "Death"/"Toto" trap of D22): a Wikidata SPARQL
/// query finds the item whose <c>wdt:P434</c> (MusicBrainz artist id) equals our
/// <see cref="Artist.Mbid"/> and returns its English Wikipedia article; the Wikipedia REST summary
/// API then yields the plain-text extract and the canonical URL (kept for CC BY-SA attribution).
/// An artist with no usable Mbid, no enwiki sitelink, or no extract resolves to <c>null</c> — a
/// gap, never a guess (Invariant 5). Parsing lives in the pure, tested
/// <see cref="WikipediaSummary"/>; this class is only the HTTP shell.
/// <para>
/// Polite to two free public services: a <see cref="FixedCadenceRateLimiter"/> at 250 ms paces the
/// (at most two) requests a lookup needs, and both clients carry an identifiable User-Agent with
/// Pedro's contact. Any transport or parse error becomes <c>null</c>.
/// </para>
/// </summary>
public sealed class WikipediaSource : IDisposable
{
    public const string WikidataClientName = "wikipedia-wikidata";
    public const string WikipediaClientName = "wikipedia-rest";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Two calls per artist against two free public endpoints; ~250 ms keeps a whole batch gentle.
    private readonly FixedCadenceRateLimiter _limiter = new(TimeSpan.FromMilliseconds(250));

    private readonly HttpClient _wikidata;
    private readonly HttpClient _wikipedia;
    private readonly ILogger<WikipediaSource> _logger;

    public WikipediaSource(HttpClient wikidata, HttpClient wikipedia, ILogger<WikipediaSource> logger)
    {
        _wikidata = wikidata;
        _wikipedia = wikipedia;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the artist's biography, or <c>null</c> when there is no accurate match. At most two
    /// requests: the Wikidata SPARQL lookup by MusicBrainz id, then — only on an enwiki hit — the
    /// Wikipedia summary. Any transport error yields <c>null</c>: a gap, never a guess.
    /// </summary>
    public async Task<WikipediaBiography?> ResolveAsync(Artist artist, CancellationToken ct)
    {
        if (artist.Mbid == Guid.Empty)
        {
            return null;
        }

        SparqlResponse? sparql = await QueryWikidataAsync(EnwikiArticleQuery(artist.Mbid), ct);
        string? title = WikipediaSummary.ParseArticleTitle(sparql);

        if (title is null)
        {
            return null;
        }

        string? summaryJson = await GetWikipediaAsync($"api/rest_v1/page/summary/{title}", ct);

        return WikipediaSummary.ParseSummary(summaryJson, title);
    }

    /// <summary>
    /// The SPARQL that finds the English-Wikipedia article for the Wikidata item carrying this
    /// MusicBrainz artist id (<c>wdt:P434</c>). The id is pinned as a literal, so Wikidata computes
    /// over a single item, never the whole graph.
    /// </summary>
    private static string EnwikiArticleQuery(Guid mbid)
    {
        // The MBID is a canonical lower-case GUID; SPARQL string literals only need quote escaping,
        // and a GUID contains none — but escape defensively all the same.
        string literal = mbid.ToString().Replace("\"", "\\\"", StringComparison.Ordinal);

        return $$"""
            SELECT ?article WHERE {
              ?item wdt:P434 "{{literal}}" .
              ?article schema:about ?item ;
                       schema:isPartOf <https://en.wikipedia.org/> .
            }
            LIMIT 1
            """;
    }

    private async Task<SparqlResponse?> QueryWikidataAsync(string sparql, CancellationToken ct)
    {
        string url = $"sparql?query={Uri.EscapeDataString(sparql)}&format=json";

        await _limiter.WaitTurnAsync(ct);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/sparql-results+json");

            using HttpResponseMessage response = await _wikidata.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Wikidata SPARQL query returned {Status}.", (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<SparqlResponse>(JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Wikidata SPARQL query failed.");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Wikidata SPARQL query returned unparseable JSON.");
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Wikidata SPARQL query timed out.");
            return null;
        }
    }

    private async Task<string?> GetWikipediaAsync(string url, CancellationToken ct)
    {
        await _limiter.WaitTurnAsync(ct);

        try
        {
            using HttpResponseMessage response = await _wikipedia.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // No summary for that title: a legitimate gap, not an error worth shouting about.
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Wikipedia summary GET {Url} returned {Status}.", url, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Wikipedia summary GET {Url} failed.", url);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Wikipedia summary GET {Url} timed out.", url);
            return null;
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
