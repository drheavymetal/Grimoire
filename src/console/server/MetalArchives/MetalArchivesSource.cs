using Grimoire.Library.Enrichment;
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
    /// Resolves a catalogue band to its Metal Archives entry. Two requests at most: a name search,
    /// then — only on a single name+country hit — the band page for themes, year and status.
    /// <para>
    /// The outcome distinguishes a band MA genuinely does not have (<see cref="EnrichmentOutcome.NoData"/>,
    /// which the caller may stamp) from MA failing to answer us (<see cref="EnrichmentOutcome.Unavailable"/>,
    /// which it must not). The pass that ran before this distinction existed stamped both alike, so
    /// every 5xx and timeout MA ever returned is recorded as "this band is not on Metallum", with no
    /// way to tell those apart from real misses after the fact (MEMORY §6f).
    /// </para>
    /// </summary>
    public async Task<MetalArchivesResult> ResolveAsync(Artist artist, CancellationToken ct)
    {
        Fetch search = await GetAsync(
            $"search/ajax-band-search/?field=name&query={Uri.EscapeDataString(artist.Name)}", ct);

        if (search.Transient)
        {
            return MetalArchivesResult.Unavailable;
        }

        if (search.Body is null)
        {
            return MetalArchivesResult.NoData;
        }

        IReadOnlyList<MetalArchivesCandidate> candidates = MetalArchivesParser.ParseSearch(search.Body);
        MetalArchivesCandidate? hit = MetalArchivesParser.Match(candidates, artist.Name, artist.Country);

        if (hit is null)
        {
            // MA answered and holds no unambiguous match: a real gap, better than the wrong band (R3).
            return MetalArchivesResult.NoData;
        }

        // The placeholder slug resolves by id (verified): /bands/_/<id> returns the band page.
        Fetch page = await GetAsync($"bands/_/{hit.Id}", ct);

        if (page.Transient)
        {
            // We know the band is on MA but could not read its page: retry rather than record a
            // match with no themes, which would look like a band whose themes MA does not list.
            return MetalArchivesResult.Unavailable;
        }

        MetalArchivesBand? band = MetalArchivesParser.ParseBand(page.Body, hit.Id, hit.Name);

        return band is null
            ? MetalArchivesResult.NoData
            : MetalArchivesResult.Matched(band);
    }

    private async Task<Fetch> GetAsync(string url, CancellationToken ct)
    {
        await _limiter.WaitTurnAsync(ct);

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // A band MA does not have: a legitimate gap, not an error worth shouting about.
                return new Fetch(false, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                // 403 in particular would mean MA has stopped serving us — surface it (R5). It is
                // also NOT transient: retrying a 403 in a loop is precisely the hammering we promised
                // them we would not do (D42/D48), so it stops at this band rather than spinning.
                _logger.LogWarning("Metal Archives GET {Url} returned {Status}.", url, (int)response.StatusCode);
                return new Fetch(HttpOutcome.IsTransient(response.StatusCode), null);
            }

            return new Fetch(false, await response.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Metal Archives GET {Url} failed.", url);
            return new Fetch(true, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Metal Archives GET {Url} timed out.", url);
            return new Fetch(true, null);
        }
    }

    /// <summary>One MA call: either a body MA stands behind, or a transient failure.</summary>
    private readonly record struct Fetch(bool Transient, string? Body);

    public void Dispose()
    {
        _limiter.Dispose();
    }
}

/// <summary>
/// One band's Metal Archives lookup outcome, with the band when <see cref="EnrichmentOutcome.Matched"/>.
/// </summary>
public readonly record struct MetalArchivesResult(EnrichmentOutcome Outcome, MetalArchivesBand? Band)
{
    /// <summary>MA has this band: store it and stamp it checked.</summary>
    public static MetalArchivesResult Matched(MetalArchivesBand band) =>
        new(EnrichmentOutcome.Matched, band);

    /// <summary>MA answered and does not have this band (or not unambiguously): stamp it checked.</summary>
    public static MetalArchivesResult NoData { get; } = new(EnrichmentOutcome.NoData, null);

    /// <summary>MA did not answer: leave the band unstamped so a later run retries it.</summary>
    public static MetalArchivesResult Unavailable { get; } = new(EnrichmentOutcome.Unavailable, null);
}
