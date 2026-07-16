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
/// Resolves artists' Wikipedia biographies in any number of editions, matched <b>only</b> by
/// MusicBrainz id so a homonym can never be mistaken for our band (the "Death"/"Toto" trap of D22):
/// a single Wikidata SPARQL query finds, for a whole <b>batch</b> of MBIDs and <b>all</b> requested
/// languages at once (two <c>VALUES</c> clauses), the items whose <c>wdt:P434</c> equals ours and
/// returns their articles; the Wikipedia REST summary API of the matching edition then yields the
/// plain-text extract and canonical URL (kept for CC BY-SA attribution) for each hit.
/// <para>
/// Batching is the point: WDQS is a shared, throttled public service, so one query per artist buries
/// the pass under timeouts and 429s — one query per ~50 artists does not. Languages ride along free:
/// asking for six editions is the same round trip as asking for one, which is what makes reaching
/// the Nordic and German underground cheap rather than a sixfold cost.
/// </para>
/// <para>
/// Every lookup is classified per language into three outcomes (<see cref="EnrichmentOutcome"/>) so
/// the caller can tell a genuine "no article" (safe to stamp as checked) apart from a transient
/// WDQS/Wikipedia failure (must <b>not</b> be stamped, or a timeout would be recorded forever as
/// "this band has no biography"). An artist with no usable Mbid, no sitelink, or no extract is a gap,
/// never a guess (Invariant 5). Parsing lives in the pure, tested <see cref="WikipediaSummary"/>.
/// </para>
/// <para>
/// Polite to two free public services: a <see cref="FixedCadenceRateLimiter"/> at 250 ms paces every
/// request (the one batched SPARQL call plus one summary call per hit, whatever edition it is
/// against), and every client carries an identifiable User-Agent with Pedro's contact.
/// </para>
/// </summary>
public sealed class WikipediaSource : IDisposable
{
    public const string WikidataClientName = "wikipedia-wikidata";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // One batched SPARQL call, then one summary call per hit, against free public endpoints;
    // ~250 ms keeps a whole batch gentle.
    private readonly FixedCadenceRateLimiter _limiter = new(TimeSpan.FromMilliseconds(250));

    private readonly HttpClient _wikidata;
    private readonly IReadOnlyDictionary<string, HttpClient> _wikipedia;
    private readonly ILogger<WikipediaSource> _logger;

    /// <summary>
    /// Each edition needs its own client because each is its own host (<c>es.wikipedia.org</c>,
    /// <c>no.wikipedia.org</c>): a single client pinned to <c>en.wikipedia.org</c> could only ever
    /// answer in English. Keyed by bare language code.
    /// </summary>
    public WikipediaSource(
        HttpClient wikidata,
        IReadOnlyDictionary<string, HttpClient> wikipediaByLanguage,
        ILogger<WikipediaSource> logger)
    {
        _wikidata = wikidata;
        _wikipedia = wikipediaByLanguage;
        _logger = logger;
    }

    /// <summary>
    /// The name of the REST client for one edition. Registration and lookup share this so a language
    /// added to configuration wires itself up.
    /// </summary>
    public static string RestClientName(string language) => $"wikipedia-rest-{language}";

    /// <summary>
    /// Resolves biographies for a whole batch in one WDQS query, then one Wikipedia summary call per
    /// article found in a language that artist was actually asked about. Returns one
    /// <see cref="BiographySet"/> per artist, keyed by MBID; within it, one
    /// <see cref="BiographyResult"/> per requested language:
    /// <list type="bullet">
    /// <item><see cref="EnrichmentOutcome.Matched"/> — an article and extract were found.</item>
    /// <item><see cref="EnrichmentOutcome.NoData"/> — WDQS/Wikipedia answered definitively that
    /// there is no article or no extract in that edition: a real gap, safe to stamp as checked.</item>
    /// <item><see cref="EnrichmentOutcome.Unavailable"/> — a transient failure (timeout, 429, 5xx):
    /// the caller must leave that language unstamped so a later run retries it.</item>
    /// </list>
    /// Languages not asked about are simply absent from the set — not "no data" — so the caller can
    /// never mistake "we did not look" for "there is nothing there".
    /// When the batch SPARQL call fails transiently, <b>every</b> artist and language in the batch
    /// comes back <see cref="EnrichmentOutcome.Unavailable"/> — nothing is stamped on a bad WDQS
    /// moment.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, BiographySet>> ResolveBatchAsync(
        IReadOnlyList<BiographyRequest> requests, CancellationToken ct)
    {
        Dictionary<Guid, BiographySet> results = new(requests.Count);

        List<BiographyRequest> usable = requests
            .Where(r => r.Artist.Mbid != Guid.Empty && r.Languages.Count > 0)
            .ToList();

        foreach (BiographyRequest request in requests.Where(r => r.Artist.Mbid == Guid.Empty))
        {
            // No MBID can never match by our accurate rule; a definitive gap in every edition alike.
            results[request.Artist.Mbid] = SetFor(request, EnrichmentOutcome.NoData);
        }

        if (usable.Count == 0)
        {
            return results;
        }

        WikidataFetch fetch = await QueryWikidataAsync(ArticlesQuery(usable), ct);

        if (fetch.Outcome == FetchOutcome.Transient)
        {
            // A bad WDQS moment must not be recorded as "these bands have no biography".
            foreach (BiographyRequest request in usable)
            {
                results[request.Artist.Mbid] = SetFor(request, EnrichmentOutcome.Unavailable);
            }

            return results;
        }

        Dictionary<string, Dictionary<string, string>> titles =
            WikipediaSummary.ParseArticleTitles(fetch.Response);

        foreach (BiographyRequest request in usable)
        {
            titles.TryGetValue(request.Artist.Mbid.ToString(), out Dictionary<string, string>? byLanguage);

            Dictionary<string, BiographyResult> byOutcome = new(StringComparer.OrdinalIgnoreCase);

            foreach (string language in request.Languages)
            {
                byOutcome[language] = await ResolveOneAsync(byLanguage, language, ct);
            }

            results[request.Artist.Mbid] = new BiographySet(byOutcome);
        }

        return results;
    }

    private async Task<BiographyResult> ResolveOneAsync(
        IReadOnlyDictionary<string, string>? byLanguage, string language, CancellationToken ct)
    {
        if (byLanguage is null || !byLanguage.TryGetValue(language, out string? title))
        {
            // WDQS answered and this item has no article in this edition: a definitive gap.
            return new BiographyResult(EnrichmentOutcome.NoData, null);
        }

        WikipediaFetch summary = await GetWikipediaAsync(language, WikipediaSummary.SummaryPath(title), ct);

        if (summary.Outcome == FetchOutcome.Transient)
        {
            return new BiographyResult(EnrichmentOutcome.Unavailable, null);
        }

        WikipediaBiography? biography = WikipediaSummary.ParseSummary(summary.Json, title, language);

        return biography is not null
            ? new BiographyResult(EnrichmentOutcome.Matched, biography)
            : new BiographyResult(EnrichmentOutcome.NoData, null);
    }

    private static BiographySet SetFor(BiographyRequest request, EnrichmentOutcome outcome)
    {
        Dictionary<string, BiographyResult> byLanguage = new(StringComparer.OrdinalIgnoreCase);

        foreach (string language in request.Languages)
        {
            byLanguage[language] = new BiographyResult(outcome, null);
        }

        return new BiographySet(byLanguage);
    }

    /// <summary>
    /// The SPARQL that finds the articles for the Wikidata items carrying any of these MusicBrainz
    /// artist ids (<c>wdt:P434</c>), in any of the requested editions. The ids are pinned as literals
    /// in a <c>VALUES</c> block, so Wikidata computes over that handful of items via the P434 index,
    /// never the whole graph — one round trip for the whole batch. A second <c>VALUES</c> block pins
    /// the editions, which is what turns "English only" into "any set of languages" without a second
    /// query.
    /// <para>
    /// Only <c>?mbid</c> and <c>?article</c> are projected. The edition is not: <c>?site</c> is by
    /// construction the article's own host, so the answer already carries its language and projecting
    /// it would only create a second copy of the same fact to keep in agreement (see
    /// <see cref="WikipediaSummary.LanguageOf"/>).
    /// </para>
    /// <para>
    /// The union of every language wanted by anyone in the batch is asked for, not each artist's own
    /// set — a single query cannot vary its filter per row. Extra sitelinks for an artist that did not
    /// need them are parsed and dropped in <see cref="ResolveBatchAsync"/>; they cost rows, never
    /// requests, because the summary call is only made for languages that artist actually asked about.
    /// </para>
    /// </summary>
    private static string ArticlesQuery(IReadOnlyList<BiographyRequest> requests)
    {
        StringBuilder values = new();

        foreach (BiographyRequest request in requests)
        {
            // A canonical lower-case GUID contains no quote to escape, but escape defensively anyway.
            string literal = request.Artist.Mbid.ToString().Replace("\"", "\\\"", StringComparison.Ordinal);
            values.Append('"').Append(literal).Append("\" ");
        }

        StringBuilder sites = new();

        IEnumerable<string> languages = requests
            .SelectMany(r => r.Languages)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.Ordinal);

        foreach (string language in languages)
        {
            sites.Append('<').Append(WikipediaSummary.SiteUrl(language)).Append("> ");
        }

        return $$"""
            SELECT ?mbid ?article WHERE {
              VALUES ?mbid { {{values.ToString().TrimEnd()}} }
              VALUES ?site { {{sites.ToString().TrimEnd()}} }
              ?item wdt:P434 ?mbid .
              ?article schema:about ?item ;
                       schema:isPartOf ?site .
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

    private async Task<WikipediaFetch> GetWikipediaAsync(string language, string url, CancellationToken ct)
    {
        if (!_wikipedia.TryGetValue(language, out HttpClient? client))
        {
            // WDQS returned an edition nobody wired a client for. That is our bug, not a verdict on
            // the band, so it must not stamp: transient, and loud in the log.
            _logger.LogWarning("No Wikipedia REST client is configured for language {Language}.", language);
            return new WikipediaFetch(FetchOutcome.Transient, null);
        }

        await _limiter.WaitTurnAsync(ct);

        try
        {
            using HttpResponseMessage response = await client.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // No summary for that title: a legitimate gap, safe to stamp as checked.
                return new WikipediaFetch(FetchOutcome.Ok, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Wikipedia {Language} summary GET {Url} returned {Status}.",
                    language, url, (int)response.StatusCode);

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
            _logger.LogWarning(ex, "Wikipedia {Language} summary GET {Url} failed.", language, url);
            return new WikipediaFetch(FetchOutcome.Transient, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Wikipedia {Language} summary GET {Url} timed out.", language, url);
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

/// <summary>
/// One artist to look up, and the editions still worth asking about for it. The caller decides the
/// languages per artist rather than the source assuming them, because "pending" is a fact about our
/// database — English is stamped on 206 882 rows and Spanish on none — and asking for a language
/// already answered would spend a request on a free service to learn nothing.
/// </summary>
public readonly record struct BiographyRequest(Artist Artist, IReadOnlyList<string> Languages);

/// <summary>One artist's biography lookup outcome, with the biography when <see cref="EnrichmentOutcome.Matched"/>.</summary>
public readonly record struct BiographyResult(EnrichmentOutcome Outcome, WikipediaBiography? Biography);

/// <summary>
/// One artist's lookup outcomes, keyed by language code. Only the languages that were asked about
/// are present: <see cref="For"/> returns null for any other, which reads as "not looked at" and is
/// deliberately not <see cref="EnrichmentOutcome.NoData"/> — the whole point of D61 is that silence
/// and "there is nothing here" are different answers.
/// </summary>
public sealed record BiographySet(IReadOnlyDictionary<string, BiographyResult> ByLanguage)
{
    public BiographyResult? For(string language) =>
        ByLanguage.TryGetValue(language, out BiographyResult result) ? result : null;
}
