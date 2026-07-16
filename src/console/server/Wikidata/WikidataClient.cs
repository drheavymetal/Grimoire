using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Wikidata;
using Grimoire.Worker.Preview;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Wikidata;

/// <summary>
/// Minimal client for the Wikidata SPARQL endpoint (<c>query.wikidata.org/sparql</c>). It sends
/// one query at a time behind a gentle rate limiter and an honest User-Agent, and asks for JSON
/// results. Batching (a <c>VALUES</c> clause of QIDs per request) is the caller's job, so the
/// endpoint is never hammered.
/// <para>
/// Queries go by <b>POST</b>, not GET. A <c>VALUES</c> block of a thousand QIDs is ~12 KB of
/// SPARQL, and as a query string that earns a <b>414 URI Too Long</b> — measured: WDQS answers 200
/// for a 500-QID batch over GET and 414 for 1 000, while the same 1 000 over POST answer in 0.44 s.
/// Capping the batch to fit a URL is what forced this pass into ~925 tiny requests where 47 do
/// (see <see cref="InfluenceJob"/>), so the transport, not the batch size, was the thing to fix.
/// </para>
/// <para>
/// A failure resolves to <see cref="EnrichmentOutcome.Unavailable"/> — never to an empty result
/// set. The two are different facts and the caller must be able to tell them apart (D61).
/// </para>
/// </summary>
public sealed class WikidataClient : IDisposable
{
    public const string HttpClientName = "wikidata";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Be polite to a shared public service: at most one query every 2 seconds. With QIDs batched a
    // thousand at a time, the whole 46k-QID catalogue is ~47 requests, so throughput is a non-issue.
    private readonly FixedCadenceRateLimiter _limiter = new(TimeSpan.FromSeconds(2));

    private readonly HttpClient _http;
    private readonly ILogger<WikidataClient> _logger;

    public WikidataClient(HttpClient http, ILogger<WikidataClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Runs a SPARQL query. Returns the parsed result set with <c>Matched</c>/<c>NoData</c> when the
    /// endpoint answered, and <c>Unavailable</c> when it did not — the caller decides what an outage
    /// means, because this class cannot know.
    /// </summary>
    public async Task<WikidataQueryResult> QueryAsync(string sparql, CancellationToken ct)
    {
        await _limiter.WaitTurnAsync(ct);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "sparql");
            request.Headers.Accept.ParseAdd("application/sparql-results+json");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["query"] = sparql,
                ["format"] = "json",
            });

            using HttpResponseMessage response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                // WikidataOutcome.FromFailedStatus makes every non-success Unavailable, deliberately
                // and regardless of the status — see the asymmetry with HttpOutcome.IsTransient
                // documented there. The status only decides how loudly this is logged.
                if (HttpOutcome.IsTransient(response.StatusCode))
                {
                    _logger.LogWarning(
                        "Wikidata SPARQL query returned {Status} (transient — the endpoint could not serve us now).",
                        (int)response.StatusCode);
                }
                else
                {
                    // 400 (bad SPARQL), 414 (batch too long for the transport), 403 (bad User-Agent):
                    // ours to fix, and it will greet every batch until we do. Say so at error level
                    // rather than letting a whole broken run read as a run of bad luck.
                    _logger.LogError(
                        "Wikidata SPARQL query returned {Status}. That is a verdict on our query, not on the "
                            + "batch — it will repeat for every batch until the query or the batch size is fixed.",
                        (int)response.StatusCode);
                }

                return new WikidataQueryResult(WikidataOutcome.FromFailedStatus(response.StatusCode), null);
            }

            SparqlResponse? parsed = await response.Content.ReadFromJsonAsync<SparqlResponse>(JsonOptions, ct);

            if (parsed is null)
            {
                _logger.LogWarning("Wikidata SPARQL query returned a null body.");
            }

            return WikidataQueryResult.FromBody(parsed);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Wikidata SPARQL query failed.");
            return WikidataQueryResult.Unavailable;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The HttpClient timeout, not our shutdown: HttpClient reports it as a cancellation, and
            // an uncaught one unwinds all the way to WorkerJob's OperationCanceledException handler,
            // which ends the entire pass with a mild "cancelled" line. One slow batch must not stop
            // the sweep — it is exactly what a deferred batch is for.
            _logger.LogWarning(ex, "Wikidata SPARQL query timed out.");
            return WikidataQueryResult.Unavailable;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Wikidata SPARQL query returned unparseable JSON.");
            return WikidataQueryResult.Unavailable;
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
