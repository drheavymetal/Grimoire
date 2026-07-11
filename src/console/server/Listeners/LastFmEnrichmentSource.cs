using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Worker.Preview;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Listeners;

/// <summary>
/// Resolves Last.fm listener counts (feature B15 / Ranks, SPEC section 6). It calls
/// <c>artist.getInfo</c> and reads <c>artist.stats.listeners</c>, the only free, non-circular
/// measure of popularity (DECISIONS D6/D31: Deezer fan counts are circular). Like every source
/// it hides behind <see cref="IEnrichmentSource"/> and a feature flag (Invariant 5 / D9): it is
/// <b>disabled unless a Last.fm API key is configured</b>. Matching is conservative (D25). When
/// the artist has a MusicBrainz id — every seeded band does — the lookup is <b>by mbid</b>, so
/// Last.fm returns exactly our entity and same-name collisions (the "Toto"/"Death" problem of
/// D22) cannot happen; if Last.fm does not index that id, the count stays null rather than borrow
/// a same-named band's listeners. Only the rare id-less artist falls back to a name lookup with
/// name verification. Paced to ~5 req/s, resilient to transient failures.
/// </summary>
public sealed class LastFmEnrichmentSource : IEnrichmentSource, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Last.fm tolerates roughly 5 requests per second on average; pace to one every 200 ms.
    private readonly FixedCadenceRateLimiter _limiter = new(TimeSpan.FromMilliseconds(200));

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<LastFmEnrichmentSource> _logger;

    public LastFmEnrichmentSource(HttpClient http, string? apiKey, ILogger<LastFmEnrichmentSource> logger)
    {
        _http = http;
        _apiKey = apiKey ?? string.Empty;
        _logger = logger;
    }

    public string Name => "Last.fm";

    /// <summary>On only when a key is present — no key, no listeners (blocker Q5).</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<ArtistEnrichment?> FetchAsync(Artist artist, CancellationToken ct)
    {
        if (!Enabled)
        {
            return null;
        }

        // Prefer an mbid lookup: Last.fm returns exactly our entity, so no name ambiguity (D25).
        // Only an id-less artist falls back to a name lookup, verified by name afterwards.
        bool byMbid = artist.Mbid != Guid.Empty;

        string url = byMbid
            ? $"2.0/?method=artist.getinfo&mbid={artist.Mbid}"
                + $"&api_key={Uri.EscapeDataString(_apiKey)}&format=json"
            : $"2.0/?method=artist.getinfo&artist={Uri.EscapeDataString(artist.Name)}"
                + $"&api_key={Uri.EscapeDataString(_apiKey)}&format=json&autocorrect=0";

        await _limiter.WaitTurnAsync(ct);

        LastFmArtistInfoResponse? response;

        try
        {
            using HttpResponseMessage http = await _http.GetAsync(url, ct);

            // Last.fm answers "artist not found" with HTTP 404 and an error body; that is a
            // legitimate gap, not a failure to log loudly.
            if (!http.IsSuccessStatusCode && http.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Last.fm getInfo for '{Name}' returned {Status}.", artist.Name, (int)http.StatusCode);
                return null;
            }

            response = await http.Content.ReadFromJsonAsync<LastFmArtistInfoResponse>(JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Last.fm getInfo for '{Name}' failed.", artist.Name);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Last.fm getInfo for '{Name}' returned unparseable JSON.", artist.Name);
            return null;
        }

        int? listeners = byMbid
            ? LastFmListeners.ParseListeners(response)
            : LastFmListeners.Resolve(response, artist.Name, artist.Mbid);

        if (listeners is null)
        {
            return null;
        }

        return new ArtistEnrichment { Listeners = listeners };
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
