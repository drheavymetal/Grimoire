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
/// <para>
/// One request returns up to 25 tracks and every matching one is kept (DECISIONS D67): the first
/// becomes <c>artists.preview_url</c> — The Rite's cut, unchanged — and the rest become the artist's
/// alternate clips. They were always in this response; they were simply dropped on the floor.
/// </para>
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

    public async Task<EnrichmentResult> FetchAsync(Artist artist, CancellationToken ct)
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
                return HttpOutcome.IsTransient(http.StatusCode)
                    ? EnrichmentResult.Unavailable
                    : EnrichmentResult.NoData;
            }

            response = await http.Content.ReadFromJsonAsync<ITunesSearchResponse>(JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "iTunes search for '{Name}' failed.", artist.Name);
            return EnrichmentResult.Unavailable;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("iTunes search for '{Name}' timed out.", artist.Name);
            return EnrichmentResult.Unavailable;
        }

        if (response is null)
        {
            // A 200 with an unreadable body is iTunes hiccuping, not a statement about the band.
            return EnrichmentResult.Unavailable;
        }

        // Every clip of the right band, not just the first. The 25 results were always paid for and 24
        // of them always thrown away (DECISIONS D67); keeping them costs no request. The match stays as
        // conservative as it ever was (D25/D22) — a wrong band never lends its audio, and D46 is what
        // that rule looks like when it slips.
        List<ITunesResult> matches = response.Results
            .Where(r => !string.IsNullOrWhiteSpace(r.PreviewUrl)
                && r.ArtistName is not null
                && NameMatch.Matches(r.ArtistName, artist.Name))
            .ToList();

        if (matches.Count == 0)
        {
            // iTunes answered and has no track under this name: genuinely inaudible here (D25).
            return EnrichmentResult.NoData;
        }

        // The first match, exactly as before: PreviewUrl is The Rite's cut and its meaning does not
        // change because alternates now travel beside it.
        ITunesResult match = matches[0];

        Dictionary<string, string> links = new(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(match.ArtistViewUrl))
        {
            links[StreamingLinks.AppleMusicKey] = match.ArtistViewUrl;
        }

        return EnrichmentResult.Matched(new ArtistEnrichment
        {
            PreviewUrl = match.PreviewUrl,
            Links = links,
            Previews = matches
                .Select(r => new PreviewCandidate(r.PreviewUrl!, Name, r.TrackName))
                .ToList(),
        });
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
