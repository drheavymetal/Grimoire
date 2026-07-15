using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Worker.Preview;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.MetalArchives;

/// <summary>
/// Fetches one band's data from Metal Archives under the terms Grimoire agreed with them (D42/D48),
/// with the cadence raised to <b>3 requests per second</b> by Pedro (D53, superseding the D42 figure):
/// a <see cref="FixedCadenceRateLimiter"/> at 333 ms, so the two calls a band needs — the search and
/// the page — pace to ~0.67 s/band, sequential, backing off on
/// 429/503 (the resilience handler on the named client), an identifiable <c>User-Agent</c> carrying
/// Pedro's contact, and never re-fetching a band already resolved (the DB's
/// <see cref="Artist.MetalArchivesCheckedAt"/> marker upstream in <see cref="MetalArchivesJob"/>).
/// <para>
/// Matching holds no MusicBrainz ids (MA has none — D48/R3), so it is by <b>name + country</b> via
/// <see cref="MetalArchivesParser.Match"/>, and an ambiguous search resolves to <c>null</c> — better
/// no match than the wrong band. The band page then yields the one field that exists nowhere else:
/// the lyrical themes (Q4).
/// </para>
/// </summary>
public sealed class MetalArchivesSource : IDisposable
{
    // MA's own request ceiling is unpublished; the agreed term is "don't hammer". We wrote "≤ 1 req/s"
    // to them, but Pedro raised the cadence to 3 req/s (D53, superseding the D42 figure) — still far
    // from hammering, and the metal-ish pool filter (MetalArchivesJob) is what actually shortened the
    // pass, not the speed-up. Kept sequential; still backs off on 429/503.
    private readonly FixedCadenceRateLimiter _limiter = new(TimeSpan.FromMilliseconds(333));

    private readonly HttpClient _http;
    private readonly ILogger<MetalArchivesSource> _logger;

    public MetalArchivesSource(HttpClient http, ILogger<MetalArchivesSource> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a catalogue band to its Metal Archives entry, or <c>null</c> when MA has no
    /// unambiguous match. Two requests at most: a name search, then — only on a single name+country
    /// hit — the band page for themes, year and status. Any transport error yields <c>null</c>: a
    /// gap, never a guess.
    /// </summary>
    public async Task<MetalArchivesBand?> ResolveAsync(Artist artist, CancellationToken ct)
    {
        string? searchJson = await GetAsync(
            $"search/ajax-band-search/?field=name&query={Uri.EscapeDataString(artist.Name)}", ct);

        if (searchJson is null)
        {
            return null;
        }

        IReadOnlyList<MetalArchivesCandidate> candidates = MetalArchivesParser.ParseSearch(searchJson);
        MetalArchivesCandidate? hit = MetalArchivesParser.Match(candidates, artist.Name, artist.Country);

        if (hit is null)
        {
            return null;
        }

        // The placeholder slug resolves by id (verified): /bands/_/<id> returns the band page.
        string? html = await GetAsync($"bands/_/{hit.Id}", ct);

        return MetalArchivesParser.ParseBand(html, hit.Id, hit.Name);
    }

    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        await _limiter.WaitTurnAsync(ct);

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // A band MA does not have: a legitimate gap, not an error worth shouting about.
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // 403 in particular would mean MA has stopped serving us — surface it (R5).
                _logger.LogWarning("Metal Archives GET {Url} returned {Status}.", url, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Metal Archives GET {Url} failed.", url);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Metal Archives GET {Url} timed out.", url);
            return null;
        }
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
