using System.Net.Http.Json;
using System.Text.Json;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Preview;

/// <summary>
/// Resolves audio previews from the iTunes Search API — the primary source (DECISIONS D25:
/// iTunes covers 41 %, more than double Deezer's 19 %). No API key; paced to ~20 req/min.
/// A result counts only when its artist name matches exactly (<see cref="NameMatch"/>), so a
/// wrong band never lends its audio. Feature-flagged via <c>Sources:ITunes:Enabled</c> (D9).
/// </summary>
public sealed class ITunesEnrichmentSource : IEnrichmentSource, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly FixedCadenceRateLimiter _limiter = new(TimeSpan.FromSeconds(3));
    private readonly ILogger<ITunesEnrichmentSource> _logger;

    public ITunesEnrichmentSource(HttpClient http, bool enabled, ILogger<ITunesEnrichmentSource> logger)
    {
        _http = http;
        Enabled = enabled;
        _logger = logger;
    }

    public string Name => "iTunes";

    public bool Enabled { get; }

    public async Task<ArtistEnrichment?> FetchAsync(Artist artist, CancellationToken ct)
    {
        string term = Uri.EscapeDataString(artist.Name);
        string url = $"search?term={term}&entity=song&limit=25";

        await _limiter.WaitTurnAsync(ct);

        ITunesSearchResponse? response;

        try
        {
            using HttpResponseMessage http = await _http.GetAsync(url, ct);

            if (!http.IsSuccessStatusCode)
            {
                _logger.LogWarning("iTunes search for '{Name}' returned {Status}.", artist.Name, (int)http.StatusCode);
                return null;
            }

            response = await http.Content.ReadFromJsonAsync<ITunesSearchResponse>(JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "iTunes search for '{Name}' failed.", artist.Name);
            return null;
        }

        if (response is null)
        {
            return null;
        }

        ITunesResult? match = response.Results
            .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.PreviewUrl)
                && r.ArtistName is not null
                && NameMatch.Matches(r.ArtistName, artist.Name));

        if (match is null)
        {
            return null;
        }

        Dictionary<string, string> links = new(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(match.ArtistViewUrl))
        {
            links[StreamingLinks.AppleMusicKey] = match.ArtistViewUrl;
        }

        return new ArtistEnrichment { PreviewUrl = match.PreviewUrl, Links = links };
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
