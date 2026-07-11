using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Library.Wikidata;
using Grimoire.Worker.Preview;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Wikidata;

/// <summary>
/// Minimal client for the Wikidata SPARQL endpoint (<c>query.wikidata.org/sparql</c>). It sends
/// one query at a time behind a gentle rate limiter and an honest User-Agent, and asks for JSON
/// results. Batching (a <c>VALUES</c> clause of QIDs per request) is the caller's job, so the
/// endpoint is never hammered. A failed or unparseable response resolves to <c>null</c> — a gap,
/// never a masked error.
/// </summary>
public sealed class WikidataClient : IDisposable
{
    public const string HttpClientName = "wikidata";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Be polite to a shared public service: at most one query every 2 seconds. With ~50 QIDs per
    // VALUES batch, the whole corpus is a handful of requests, so throughput is a non-issue.
    private readonly FixedCadenceRateLimiter _limiter = new(TimeSpan.FromSeconds(2));

    private readonly HttpClient _http;
    private readonly ILogger<WikidataClient> _logger;

    public WikidataClient(HttpClient http, ILogger<WikidataClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Runs a SPARQL query and returns the parsed result set, or null on any failure.</summary>
    public async Task<SparqlResponse?> QueryAsync(string sparql, CancellationToken ct)
    {
        string url = $"sparql?query={Uri.EscapeDataString(sparql)}&format=json";

        await _limiter.WaitTurnAsync(ct);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/sparql-results+json");

            using HttpResponseMessage response = await _http.SendAsync(request, ct);

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
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
