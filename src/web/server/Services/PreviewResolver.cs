using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grimoire.Library.Services;

namespace Grimoire.Server.Services;

/// <summary>A preview URL resolved just-in-time, tagged with the source that gave it (for logs).</summary>
public sealed record PreviewResolution(string Url, string Source);

/// <summary>
/// Resolves a ~30 s preview URL for one band <b>at serve time</b> (DECISIONS D25/D19). The catalogue
/// grew to 207k artists and the Rite can no longer pre-resolve a preview for all of them under the
/// iTunes ~20 req/min ceiling, so the engine serves from the embedded catalogue and the audio URL is
/// resolved on demand and cached on <c>artists.preview_url</c> (see <see cref="RiteController"/>).
///
/// <para>
/// iTunes is asked first and Deezer only complements it — never the reverse (DECISIONS D25: iTunes
/// covers 41 %, more than double Deezer's 19 %). Matching is conservative (<see cref="NameMatch"/>):
/// a wrong band never lends its audio, so an honest null beats the wrong preview. Grimoire never
/// downloads audio (Invariant 4 / DECISIONS D10) — this only resolves the URL; the audio itself is
/// streamed later, host-side, through <see cref="PreviewAudioProxy"/>.
/// </para>
///
/// <para>
/// A singleton: it holds the cross-request pacing gates (one per host) so a burst of candidate
/// resolutions inside a single serve — and across concurrent serves — stays polite to the two free
/// APIs. It owns no per-request state and no DbContext.
/// </para>
/// </summary>
public sealed class PreviewResolver
{
    /// <summary>Named typed clients configured in Program.cs (base address, short timeout, retry).</summary>
    public const string ITunesClientName = "preview-itunes";
    public const string DeezerClientName = "preview-deezer";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>MusicBrainz "free streaming" relations that point at a Deezer artist page look like this.</summary>
    private const string DeezerArtistMarker = "deezer.com/artist/";

    private readonly IHttpClientFactory _factory;

    // Interactive JIT cannot use the ETL's 3 s bulk pace (a serve that probes several candidates would
    // take half a minute); it paces just enough to avoid bursts, and leans on the retry handler for a
    // stray 429. iTunes is the primary source so it is probed more often; Deezer only complements it.
    private readonly MinIntervalGate _itunesGate = new(TimeSpan.FromMilliseconds(600));
    private readonly MinIntervalGate _deezerGate = new(TimeSpan.FromMilliseconds(350));

    private readonly ILogger<PreviewResolver> _logger;

    public PreviewResolver(IHttpClientFactory factory, ILogger<PreviewResolver> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a preview URL for <paramref name="artistName"/>, or null when neither source has audio
    /// (roughly half the underground is genuinely inaudible — DECISIONS D25 — so null is a real gap,
    /// never invented). <paramref name="links"/> is the artist's stored link map: when it carries a
    /// MusicBrainz "free streaming" relation to Deezer, that exact artist id is used instead of a name
    /// search (an unambiguous mapping — no name matching needed).
    /// </summary>
    public async Task<PreviewResolution?> ResolveAsync(
        string artistName,
        IReadOnlyDictionary<string, string>? links,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return null;
        }

        // iTunes first (DECISIONS D25) — never the other way round.
        string? itunes = await ResolveITunesAsync(artistName, ct);

        if (itunes is not null)
        {
            _logger.LogInformation("Resolved a preview for '{Name}' from iTunes.", artistName);
            return new PreviewResolution(itunes, "iTunes");
        }

        // Deezer as the complement.
        string? deezer = await ResolveDeezerAsync(artistName, links, ct);

        if (deezer is not null)
        {
            _logger.LogInformation("Resolved a preview for '{Name}' from Deezer.", artistName);
            return new PreviewResolution(deezer, "Deezer");
        }

        return null;
    }

    private async Task<string?> ResolveITunesAsync(string artistName, CancellationToken ct)
    {
        string term = Uri.EscapeDataString(artistName);
        string url = $"search?term={term}&entity=song&limit=25";

        ITunesSearchResponse? response = await GetAsync<ITunesSearchResponse>(
            ITunesClientName, _itunesGate, url, artistName, ct);

        if (response is null)
        {
            return null;
        }

        // Exact normalised name match only, and a non-empty preview (DECISIONS D25): a wrong band
        // never lends its audio.
        ITunesResult? match = response.Results.FirstOrDefault(r =>
            !string.IsNullOrWhiteSpace(r.PreviewUrl)
            && r.ArtistName is not null
            && NameMatch.Matches(r.ArtistName, artistName));

        return match?.PreviewUrl;
    }

    private async Task<string?> ResolveDeezerAsync(
        string artistName,
        IReadOnlyDictionary<string, string>? links,
        CancellationToken ct)
    {
        // Prefer the exact Deezer id MusicBrainz already asserts for this artist, if present: it is an
        // unambiguous mapping to our entity, so no name matching is needed.
        long? knownId = DeezerArtistId(links);

        if (knownId is long id)
        {
            return await FetchDeezerTopPreviewAsync(id, ct);
        }

        DeezerListResponse<DeezerArtist>? search = await GetAsync<DeezerListResponse<DeezerArtist>>(
            DeezerClientName,
            _deezerGate,
            $"search/artist?q={Uri.EscapeDataString(artistName)}&limit=5",
            artistName,
            ct);

        DeezerArtist? match = search?.Data.FirstOrDefault(a =>
            a.Name is not null && NameMatch.Matches(a.Name, artistName));

        if (match is null)
        {
            return null;
        }

        return await FetchDeezerTopPreviewAsync(match.Id, ct);
    }

    private async Task<string?> FetchDeezerTopPreviewAsync(long artistId, CancellationToken ct)
    {
        DeezerListResponse<DeezerTrack>? top = await GetAsync<DeezerListResponse<DeezerTrack>>(
            DeezerClientName,
            _deezerGate,
            $"artist/{artistId}/top?limit=1",
            artistId.ToString(),
            ct);

        string? preview = top?.Data.FirstOrDefault()?.Preview;

        return string.IsNullOrWhiteSpace(preview) ? null : preview;
    }

    /// <summary>Extracts the Deezer artist id from a "free streaming" link to deezer.com/artist/{id}.</summary>
    private static long? DeezerArtistId(IReadOnlyDictionary<string, string>? links)
    {
        if (links is null)
        {
            return null;
        }

        foreach (string value in links.Values)
        {
            int marker = value.IndexOf(DeezerArtistMarker, StringComparison.OrdinalIgnoreCase);

            if (marker < 0)
            {
                continue;
            }

            int start = marker + DeezerArtistMarker.Length;
            int end = start;

            while (end < value.Length && char.IsDigit(value[end]))
            {
                end++;
            }

            if (end > start && long.TryParse(value[start..end], out long id))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// One paced GET, deserialised. Returns null on a non-success status or a transient failure
    /// (network error or timeout): an external source that will not answer is a missing preview, not
    /// an exception to surface. This mirrors the ETL's honest degradation (Invariant 5) — it does not
    /// mask a bug in our own code, only an absent answer from a third party.
    /// </summary>
    private async Task<T?> GetAsync<T>(string clientName, MinIntervalGate gate, string url, string context, CancellationToken ct)
        where T : class
    {
        await gate.WaitAsync(ct);

        HttpClient http = _factory.CreateClient(clientName);

        try
        {
            using HttpResponseMessage response = await http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Client} request for '{Context}' returned {Status}.", clientName, context, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "{Client} request for '{Context}' failed.", clientName, context);
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // The client's own short timeout fired (not the caller cancelling): treat as no preview.
            _logger.LogWarning("{Client} request for '{Context}' timed out.", clientName, context);
            return null;
        }
    }

    /// <summary>
    /// Serialises outbound calls to a host and holds them at least <c>interval</c> apart, so a burst of
    /// candidate resolutions in one serve — or across concurrent serves — never stampedes a free API.
    /// </summary>
    private sealed class MinIntervalGate
    {
        private readonly SemaphoreSlim _mutex = new(1, 1);
        private readonly TimeSpan _interval;
        private DateTimeOffset _earliestNext = DateTimeOffset.MinValue;

        public MinIntervalGate(TimeSpan interval)
        {
            _interval = interval;
        }

        public async Task WaitAsync(CancellationToken ct)
        {
            await _mutex.WaitAsync(ct);

            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;

                if (now < _earliestNext)
                {
                    await Task.Delay(_earliestNext - now, ct);
                }

                _earliestNext = DateTimeOffset.UtcNow + _interval;
            }
            finally
            {
                _mutex.Release();
            }
        }
    }

    // -- Minimal DTOs for the two search APIs (web-server local; distinct from the worker's copies) --

    private sealed class ITunesSearchResponse
    {
        [JsonPropertyName("results")]
        public List<ITunesResult> Results { get; set; } = [];
    }

    private sealed class ITunesResult
    {
        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("previewUrl")]
        public string? PreviewUrl { get; set; }
    }

    private sealed class DeezerListResponse<T>
    {
        [JsonPropertyName("data")]
        public List<T> Data { get; set; } = [];
    }

    private sealed class DeezerArtist
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class DeezerTrack
    {
        [JsonPropertyName("preview")]
        public string? Preview { get; set; }
    }
}
