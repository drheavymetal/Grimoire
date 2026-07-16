using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Library.Wikidata;
using Grimoire.Worker.Preview;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Wikipedia;

/// <summary>
/// Resolves artists' English-Wikipedia biographies, matched <b>only</b> by MusicBrainz id so a
/// homonym can never be mistaken for our band (the "Death"/"Toto" trap of D22): a single Wikidata
/// SPARQL query finds, for a whole <b>batch</b> of MBIDs at once (a <c>VALUES</c> clause), the items
/// whose <c>wdt:P434</c> equals ours and returns their English Wikipedia articles; the Wikipedia REST
/// summary API then yields the plain-text extract and canonical URL (kept for CC BY-SA attribution)
/// for each hit. Batching is the point: WDQS is a shared, throttled public service, so one query per
/// artist buries the pass under timeouts and 429s — one query per ~50 artists does not.
/// <para>
/// Every lookup is classified into three outcomes (<see cref="EnrichmentOutcome"/>) so the caller can
/// tell a genuine "no article" (safe to stamp as checked) apart from a transient WDQS/Wikipedia
/// failure (must <b>not</b> be stamped, or a timeout would be recorded forever as "this band has no
/// biography"). An artist with no usable Mbid, no enwiki sitelink, or no extract is a gap, never a
/// guess (Invariant 5). Parsing lives in the pure, tested <see cref="WikipediaSummary"/>.
/// </para>
/// <para>
/// Polite to two free public services: a <see cref="FixedCadenceRateLimiter"/> at 250 ms paces every
/// request (the one batched SPARQL call plus one summary call per hit), and both clients carry an
/// identifiable User-Agent with Pedro's contact.
/// </para>
/// </summary>
public sealed class WikipediaSource : IDisposable
{
    public const string WikidataClientName = "wikipedia-wikidata";
    public const string WikipediaClientName = "wikipedia-rest";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // One batched SPARQL call, then one summary call per hit, against two free public endpoints;
    // ~250 ms keeps a whole batch gentle.
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
    /// Resolves biographies for a whole batch of artists in one WDQS query, then one Wikipedia summary
    /// call per enwiki hit. Returns one <see cref="BiographyResult"/> per artist, keyed by MBID:
    /// <list type="bullet">
    /// <item><see cref="EnrichmentOutcome.Matched"/> — an article and extract were found.</item>
    /// <item><see cref="EnrichmentOutcome.NoData"/> — WDQS/Wikipedia answered definitively that
    /// there is no article or no extract: a real gap, safe to stamp as checked.</item>
    /// <item><see cref="EnrichmentOutcome.Unavailable"/> — a transient failure (timeout, 429, 5xx):
    /// the caller must leave the artist unstamped so a later run retries it.</item>
    /// </list>
    /// When the batch SPARQL call fails transiently, <b>every</b> artist in the batch comes back
    /// <see cref="EnrichmentOutcome.Unavailable"/> — nothing is stamped on a bad WDQS moment.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, BiographyResult>> ResolveBatchAsync(
        IReadOnlyList<Artist> artists, CancellationToken ct)
    {
        Dictionary<Guid, BiographyResult> results = new(artists.Count);

        List<Artist> usable = artists.Where(a => a.Mbid != Guid.Empty).ToList();

        foreach (Artist artist in artists.Where(a => a.Mbid == Guid.Empty))
        {
            // No MBID can never match by our accurate rule; a definitive gap, safe to stamp.
            results[artist.Mbid] = new BiographyResult(EnrichmentOutcome.NoData, null);
        }

        if (usable.Count == 0)
        {
            return results;
        }

        WikidataFetch fetch = await QueryWikidataAsync(EnwikiArticlesQuery(usable), ct);

        if (fetch.Outcome == FetchOutcome.Transient)
        {
            // A bad WDQS moment must not be recorded as "these bands have no biography".
            foreach (Artist artist in usable)
            {
                results[artist.Mbid] = new BiographyResult(EnrichmentOutcome.Unavailable, null);
            }

            return results;
        }

        Dictionary<string, string> titles = WikipediaSummary.ParseArticleTitles(fetch.Response);

        foreach (Artist artist in usable)
        {
            if (!titles.TryGetValue(artist.Mbid.ToString(), out string? title))
            {
                // WDQS answered and this item has no enwiki article: a definitive gap.
                results[artist.Mbid] = new BiographyResult(EnrichmentOutcome.NoData, null);
                continue;
            }

            WikipediaFetch summary = await GetWikipediaAsync(WikipediaSummary.SummaryPath(title), ct);

            if (summary.Outcome == FetchOutcome.Transient)
            {
                results[artist.Mbid] = new BiographyResult(EnrichmentOutcome.Unavailable, null);
                continue;
            }

            WikipediaBiography? biography = WikipediaSummary.ParseSummary(summary.Json, title);

            results[artist.Mbid] = biography is not null
                ? new BiographyResult(EnrichmentOutcome.Matched, biography)
                : new BiographyResult(EnrichmentOutcome.NoData, null);
        }

        return results;
    }

    /// <summary>
    /// The SPARQL that finds the English-Wikipedia articles for the Wikidata items carrying any of
    /// these MusicBrainz artist ids (<c>wdt:P434</c>). The ids are pinned as literals in a
    /// <c>VALUES</c> block, so Wikidata computes over that handful of items via the P434 index, never
    /// the whole graph — one round trip for the whole batch. Both <c>?mbid</c> and <c>?article</c> are
    /// projected so each article maps back to the artist that asked for it.
    /// </summary>
    private static string EnwikiArticlesQuery(IReadOnlyList<Artist> artists)
    {
        StringBuilder values = new();

        foreach (Artist artist in artists)
        {
            // A canonical lower-case GUID contains no quote to escape, but escape defensively anyway.
            string literal = artist.Mbid.ToString().Replace("\"", "\\\"", StringComparison.Ordinal);
            values.Append('"').Append(literal).Append("\" ");
        }

        return $$"""
            SELECT ?mbid ?article WHERE {
              VALUES ?mbid { {{values.ToString().TrimEnd()}} }
              ?item wdt:P434 ?mbid .
              ?article schema:about ?item ;
                       schema:isPartOf <https://en.wikipedia.org/> .
            }
            """;
    }

    private async Task<WikidataFetch> QueryWikidataAsync(string sparql, CancellationToken ct)
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
                // Every non-success is transient here, deliberately — unlike the summary endpoint,
                // where a 4xx is a definitive verdict on one title. A 4xx from WDQS is a verdict on
                // OUR query, not on the artists in it: it would arrive for every batch alike, and
                // stamping on it would write "has no biography" across the whole catalogue on the
                // strength of a bug in this file. A pass that spins is a loud failure; a pass that
                // stamps a lie is a silent one. Prefer the loud one.
                _logger.LogWarning("Wikidata SPARQL query returned {Status}.", (int)response.StatusCode);
                return new WikidataFetch(FetchOutcome.Transient, null);
            }

            SparqlResponse? parsed = await response.Content.ReadFromJsonAsync<SparqlResponse>(JsonOptions, ct);
            return new WikidataFetch(FetchOutcome.Ok, parsed);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Wikidata SPARQL query failed.");
            return new WikidataFetch(FetchOutcome.Transient, null);
        }
        catch (JsonException ex)
        {
            // A truncated/garbled body is a WDQS hiccup, not proof the bands lack articles: transient.
            _logger.LogWarning(ex, "Wikidata SPARQL query returned unparseable JSON.");
            return new WikidataFetch(FetchOutcome.Transient, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Wikidata SPARQL query timed out.");
            return new WikidataFetch(FetchOutcome.Transient, null);
        }
    }

    private async Task<WikipediaFetch> GetWikipediaAsync(string url, CancellationToken ct)
    {
        await _limiter.WaitTurnAsync(ct);

        try
        {
            using HttpResponseMessage response = await _wikipedia.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No summary for that title: a legitimate gap, safe to stamp as checked.
                return new WikipediaFetch(FetchOutcome.Ok, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Wikipedia summary GET {Url} returned {Status}.", url, (int)response.StatusCode);

                // Only 408/429/5xx are worth retrying. Treating every non-success as transient meant
                // a 400 from an unusable title was retried on every run, for ever, and the pass could
                // never drain (MEMORY §6f) — a permanent answer must be allowed to be permanent.
                return HttpOutcome.IsTransient(response.StatusCode)
                    ? new WikipediaFetch(FetchOutcome.Transient, null)
                    : new WikipediaFetch(FetchOutcome.Ok, null);
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            return new WikipediaFetch(FetchOutcome.Ok, json);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Wikipedia summary GET {Url} failed.", url);
            return new WikipediaFetch(FetchOutcome.Transient, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Wikipedia summary GET {Url} timed out.", url);
            return new WikipediaFetch(FetchOutcome.Transient, null);
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }

    private enum FetchOutcome
    {
        Ok,
        Transient,
    }

    private readonly record struct WikidataFetch(FetchOutcome Outcome, SparqlResponse? Response);

    private readonly record struct WikipediaFetch(FetchOutcome Outcome, string? Json);
}

/// <summary>One artist's biography lookup outcome, with the biography when <see cref="EnrichmentOutcome.Matched"/>.</summary>
public readonly record struct BiographyResult(EnrichmentOutcome Outcome, WikipediaBiography? Biography);
