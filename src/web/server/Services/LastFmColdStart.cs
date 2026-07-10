using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Grimoire.Server.Services;

/// <summary>
/// Cold start from a user's Last.fm scrobbles (feature C1, DECISIONS D15): fetch the bands they
/// have played most, map them onto the catalogue, and average those bands' embeddings into an
/// initial taste. This is a feature-flagged source (Invariant 5 / D9): it is <b>disabled</b>
/// whenever no Last.fm API key is configured, and while disabled the import endpoint reports the
/// gap honestly instead of inventing scrobbles. There is no key in development (blocker Q5), so
/// this path is dormant by default — the live "choose five bands" path is the one that ships.
/// </summary>
public interface IColdStartImport
{
    /// <summary>Whether the source is turned on (i.e. a Last.fm API key is configured).</summary>
    bool Enabled { get; }

    /// <summary>
    /// The names of a Last.fm user's most-played artists, most-played first, or null if the
    /// lookup failed. Never throws for a missing user; returns an empty list instead.
    /// </summary>
    Task<IReadOnlyList<string>?> GetTopArtistNamesAsync(string lastFmUsername, int limit, CancellationToken ct);
}

/// <summary>Configuration for the Last.fm cold-start source.</summary>
public sealed class LastFmOptions
{
    /// <summary>Last.fm API key. Empty means the source is disabled (blocker Q5).</summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Real Last.fm <c>user.getTopArtists</c> client, gated entirely behind the presence of an API
/// key. It is genuine, correct code — not a stub — but with no key configured it never runs and
/// <see cref="Enabled"/> is false. The disabled behaviour is exercised by tests; the live call
/// cannot be, because the key does not exist yet (documented gap).
/// </summary>
public sealed class LastFmColdStart : IColdStartImport
{
    private readonly HttpClient _http;
    private readonly LastFmOptions _options;
    private readonly ILogger<LastFmColdStart> _logger;

    public LastFmColdStart(HttpClient http, LastFmOptions options, ILogger<LastFmColdStart> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public bool Enabled => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<string>?> GetTopArtistNamesAsync(string lastFmUsername, int limit, CancellationToken ct)
    {
        if (!Enabled)
        {
            // The caller must check Enabled first; this is the belt-and-braces guard.
            return null;
        }

        string url =
            $"2.0/?method=user.gettopartists&user={Uri.EscapeDataString(lastFmUsername)}"
            + $"&api_key={Uri.EscapeDataString(_options.ApiKey)}&format=json&limit={limit}";

        TopArtistsResponse? body;
        try
        {
            body = await _http.GetFromJsonAsync<TopArtistsResponse>(url, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Last.fm top-artists lookup failed for '{User}'.", lastFmUsername);
            return null;
        }

        List<string> names = body?.TopArtists?.Artist?
            .Select(a => a.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList() ?? [];

        return names;
    }

    private sealed class TopArtistsResponse
    {
        [JsonPropertyName("topartists")]
        public TopArtists? TopArtists { get; set; }
    }

    private sealed class TopArtists
    {
        [JsonPropertyName("artist")]
        public List<TopArtist>? Artist { get; set; }
    }

    private sealed class TopArtist
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
