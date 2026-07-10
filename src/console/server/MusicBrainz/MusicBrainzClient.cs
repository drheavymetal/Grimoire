using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.MusicBrainz;

/// <summary>
/// Typed client for the MusicBrainz WS/2 JSON API. Every call goes through the
/// shared rate limiter (1 req/s). Transient failures and 429/503 are handled by the
/// resilience handler configured on the named HttpClient.
/// </summary>
public class MusicBrainzClient
{
    public const string HttpClientName = "musicbrainz";

    // Bounded tag list (DECISIONS D23 / SPEC section 2): the four core metal tags plus
    // the folk corpus that orbits metal. The bare tag "folk" is deliberately excluded —
    // it drags in the whole folk canon and destroys the corpus. Groups only.
    private const string SearchQuery =
        "(tag:\"black metal\" OR tag:\"death metal\" OR tag:\"doom metal\" OR tag:\"heavy metal\" " +
        "OR tag:\"viking folk\" OR tag:\"nordic folk\" OR tag:\"neofolk\" OR tag:\"pagan folk\" " +
        "OR tag:\"celtic folk\" OR tag:\"dark folk\" OR tag:\"folk metal\" OR tag:\"ritual folk\") " +
        "AND type:group";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly MusicBrainzRateLimiter _limiter;
    private readonly ILogger<MusicBrainzClient> _logger;

    public MusicBrainzClient(HttpClient http, MusicBrainzRateLimiter limiter, ILogger<MusicBrainzClient> logger)
    {
        _http = http;
        _limiter = limiter;
        _logger = logger;
    }

    public async Task<ArtistSearchResponse?> SearchArtistsAsync(int offset, int limit, CancellationToken ct)
    {
        string url = $"artist?query={Uri.EscapeDataString(SearchQuery)}&limit={limit}&offset={offset}&fmt=json";
        return await GetAsync<ArtistSearchResponse>(url, ct);
    }

    /// <summary>
    /// Searches for an anchor artist by exact name. Returns all candidates so the caller
    /// can decide whether the match is unambiguous; this method never guesses.
    /// </summary>
    public async Task<ArtistSearchResponse?> SearchArtistByNameAsync(string name, CancellationToken ct)
    {
        string url = $"artist?query=artist:{Uri.EscapeDataString($"\"{name}\"")}&limit=10&fmt=json";
        return await GetAsync<ArtistSearchResponse>(url, ct);
    }

    public async Task<MbArtist?> GetArtistAsync(string mbid, CancellationToken ct)
    {
        string url = $"artist/{mbid}?inc=tags+url-rels&fmt=json";
        return await GetAsync<MbArtist>(url, ct);
    }

    /// <summary>
    /// Fetches an artist with its artist-artist relations (band membership with dates and
    /// instruments). Feeds the lineup timeline and Bloodline (features B7/B8/B16).
    /// </summary>
    public async Task<MbArtist?> GetArtistRelationsAsync(string mbid, CancellationToken ct)
    {
        string url = $"artist/{mbid}?inc=artist-rels&fmt=json";
        return await GetAsync<MbArtist>(url, ct);
    }

    public async Task<ReleaseGroupResponse?> GetReleaseGroupsAsync(string artistMbid, CancellationToken ct)
    {
        string url = $"release-group?artist={artistMbid}&limit=25&fmt=json";
        return await GetAsync<ReleaseGroupResponse>(url, ct);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
        where T : class
    {
        await _limiter.WaitTurnAsync(ct);

        using HttpResponseMessage response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("MusicBrainz request '{Url}' returned {Status}.", url, (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }
}
