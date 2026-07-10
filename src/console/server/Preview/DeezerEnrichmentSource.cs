using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Preview;

/// <summary>
/// Resolves the exact Deezer link and, as a complement to iTunes, a preview (DECISIONS D25:
/// Deezer covers 19 %, adding ~11 points iTunes lacks). No auth. Two calls per artist: a name
/// search for the exact artist link and id, then the artist's top track for a preview. Matches
/// exactly (<see cref="NameMatch"/>) so a wrong artist never contributes. Feature-flagged via
/// <c>Sources:Deezer:Enabled</c> (D9).
/// </summary>
public sealed class DeezerEnrichmentSource : IEnrichmentSource, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly FixedCadenceRateLimiter _limiter = new(TimeSpan.FromSeconds(1));
    private readonly ILogger<DeezerEnrichmentSource> _logger;

    public DeezerEnrichmentSource(HttpClient http, bool enabled, ILogger<DeezerEnrichmentSource> logger)
    {
        _http = http;
        Enabled = enabled;
        _logger = logger;
    }

    public string Name => "Deezer";

    public bool Enabled { get; }

    public async Task<ArtistEnrichment?> FetchAsync(Artist artist, CancellationToken ct)
    {
        DeezerArtist? match = await FindArtistAsync(artist.Name, ct);

        if (match is null)
        {
            return null;
        }

        Dictionary<string, string> links = new(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(match.Link))
        {
            links[StreamingLinks.DeezerKey] = match.Link;
        }

        string? preview = await FetchTopPreviewAsync(match.Id, ct);

        return new ArtistEnrichment { PreviewUrl = preview, Links = links };
    }

    private async Task<DeezerArtist?> FindArtistAsync(string name, CancellationToken ct)
    {
        string url = $"search/artist?q={Uri.EscapeDataString(name)}&limit=5";
        DeezerListResponse<DeezerArtist>? response = await GetAsync<DeezerListResponse<DeezerArtist>>(url, name, ct);

        return response?.Data.FirstOrDefault(a => a.Name is not null && NameMatch.Matches(a.Name, name));
    }

    private async Task<string?> FetchTopPreviewAsync(long artistId, CancellationToken ct)
    {
        string url = $"artist/{artistId}/top?limit=1";
        DeezerListResponse<DeezerTrack>? response = await GetAsync<DeezerListResponse<DeezerTrack>>(url, artistId.ToString(), ct);

        string? preview = response?.Data.FirstOrDefault()?.Preview;

        return string.IsNullOrWhiteSpace(preview) ? null : preview;
    }

    private async Task<T?> GetAsync<T>(string url, string context, CancellationToken ct)
        where T : class
    {
        await _limiter.WaitTurnAsync(ct);

        try
        {
            using HttpResponseMessage http = await _http.GetAsync(url, ct);

            if (!http.IsSuccessStatusCode)
            {
                _logger.LogWarning("Deezer request for '{Context}' returned {Status}.", context, (int)http.StatusCode);
                return null;
            }

            return await http.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Deezer request for '{Context}' failed.", context);
            return null;
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
