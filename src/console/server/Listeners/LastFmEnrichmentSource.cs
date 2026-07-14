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

        // 1. Precise: mbid lookup. Last.fm returns exactly our entity, so a same-name collision
        //    (the "Toto"/"Death" problem of D22) cannot happen. No name verification needed.
        if (artist.Mbid != Guid.Empty)
        {
            LastFmArtistInfoResponse? byId = await GetInfoAsync(
                $"2.0/?method=artist.getinfo&mbid={artist.Mbid}&api_key={Uri.EscapeDataString(_apiKey)}&format=json",
                artist.Name, ct);

            int? listeners = LastFmListeners.ParseListeners(byId);
            if (listeners is not null)
            {
                // Tags ride along in the same body — one call fills both (MEMORY §6b).
                return new ArtistEnrichment { Listeners = listeners, Tags = LastFmListeners.ParseTags(byId) };
            }
        }

        // 2. Fallback by name (D41). Last.fm frequently indexes a band under a different mbid than
        //    MusicBrainz, so the id lookup misses it — even famous bands. The name lookup recovers
        //    them; ResolveByName verifies the returned name matches (so a same-name band can't lend
        //    its count) but accepts a differing mbid. autocorrect=0 keeps Last.fm from silently
        //    redirecting to a different band.
        LastFmArtistInfoResponse? byName = await GetInfoAsync(
            $"2.0/?method=artist.getinfo&artist={Uri.EscapeDataString(artist.Name)}"
                + $"&api_key={Uri.EscapeDataString(_apiKey)}&format=json&autocorrect=0",
            artist.Name, ct);

        int? named = LastFmListeners.ResolveByName(byName, artist.Name);
        return named is null
            ? null
            : new ArtistEnrichment { Listeners = named, Tags = LastFmListeners.ParseTags(byName) };
    }

    private async Task<LastFmArtistInfoResponse?> GetInfoAsync(string url, string name, CancellationToken ct)
    {
        await _limiter.WaitTurnAsync(ct);

        try
        {
            using HttpResponseMessage http = await _http.GetAsync(url, ct);

            // Last.fm answers "artist not found" with HTTP 404 and an error body; that is a
            // legitimate gap, not a failure to log loudly.
            if (!http.IsSuccessStatusCode && http.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Last.fm getInfo for '{Name}' returned {Status}.", name, (int)http.StatusCode);
                return null;
            }

            return await http.Content.ReadFromJsonAsync<LastFmArtistInfoResponse>(JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Last.fm getInfo for '{Name}' failed.", name);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Last.fm getInfo for '{Name}' returned unparseable JSON.", name);
            return null;
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
