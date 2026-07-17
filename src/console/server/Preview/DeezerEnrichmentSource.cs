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
/// search for the exact artist link and id, then the artist's top tracks for previews. Matches
/// exactly (<see cref="NameMatch"/>) so a wrong artist never contributes. Feature-flagged via
/// <c>Sources:Deezer:Enabled</c> (D9).
/// <para>
/// The top-tracks call asks for five (DECISIONS D67), still one request: the first playable one is the
/// preview Deezer contributes to <c>artists.preview_url</c> exactly as before, and the rest become
/// alternate clips.
/// </para>
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

    public async Task<EnrichmentResult> FetchAsync(Artist artist, CancellationToken ct)
    {
        Fetch<DeezerListResponse<DeezerArtist>> search = await GetAsync<DeezerListResponse<DeezerArtist>>(
            $"search/artist?q={Uri.EscapeDataString(artist.Name)}&limit=5", artist.Name, ct);

        if (search.Transient)
        {
            return EnrichmentResult.Unavailable;
        }

        DeezerArtist? match = search.Value?.Data
            .FirstOrDefault(a => a.Name is not null && NameMatch.Matches(a.Name, artist.Name));

        if (match is null)
        {
            // Deezer answered and has no artist under this name: a real gap (D25).
            return EnrichmentResult.NoData;
        }

        Dictionary<string, string> links = new(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(match.Link))
        {
            links[StreamingLinks.DeezerKey] = match.Link;
        }

        // Five, not one. The old limit=1 was the stingiest form of the same waste iTunes had: we asked
        // for exactly one clip of a band we had already spent a search on, so a second clip was
        // impossible by construction rather than by any limit of Deezer's (DECISIONS D67). Widening it
        // is one number and no extra request. Five, and not the maximum, because these are alternates
        // for a guessing game, not an index of the band (Invariant 4).
        Fetch<DeezerListResponse<DeezerTrack>> top = await GetAsync<DeezerListResponse<DeezerTrack>>(
            $"artist/{match.Id}/top?limit=5", match.Id.ToString(), ct);

        if (top.Transient)
        {
            // We found the artist but could not ask for their audio: do not settle for a
            // half-answer that would record them as having no preview.
            return EnrichmentResult.Unavailable;
        }

        // The artist id came from an exact name match (or from Deezer's own top list for it), so every
        // track here is that band's — no per-track name check to make.
        List<DeezerTrack> tracks = top.Value?.Data
            .Where(t => !string.IsNullOrWhiteSpace(t.Preview))
            .ToList() ?? [];

        string? preview = tracks.FirstOrDefault()?.Preview;

        return EnrichmentResult.Matched(new ArtistEnrichment
        {
            PreviewUrl = string.IsNullOrWhiteSpace(preview) ? null : preview,
            Links = links,
            Previews = tracks
                .Select(t => new PreviewCandidate(t.Preview!, Name, t.Title))
                .ToList(),
        });
    }

    private async Task<Fetch<T>> GetAsync<T>(string url, string context, CancellationToken ct)
        where T : class
    {
        await _limiter.WaitTurnAsync(ct);

        try
        {
            using HttpResponseMessage http = await _http.GetAsync(url, ct);

            if (!http.IsSuccessStatusCode)
            {
                _logger.LogWarning("Deezer request for '{Context}' returned {Status}.", context, (int)http.StatusCode);
                return new Fetch<T>(HttpOutcome.IsTransient(http.StatusCode), null);
            }

            return new Fetch<T>(false, await http.Content.ReadFromJsonAsync<T>(JsonOptions, ct));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Deezer request for '{Context}' failed.", context);
            return new Fetch<T>(true, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Deezer request for '{Context}' timed out.", context);
            return new Fetch<T>(true, null);
        }
    }

    /// <summary>One Deezer call: either a body Deezer stands behind, or a transient failure.</summary>
    private readonly record struct Fetch<T>(bool Transient, T? Value)
        where T : class;

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
