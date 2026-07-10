using Microsoft.Extensions.Options;

namespace Grimoire.Server.Services;

/// <summary>Where the cover proxy stores images and negative (404) markers on disk.</summary>
public sealed class CoverCacheOptions
{
    /// <summary>
    /// Directory for the on-disk cache. Defaults to a per-machine temp folder so nothing
    /// lands inside the repo; production overrides it via <c>CoverCache__Directory</c> onto a
    /// mounted volume.
    /// </summary>
    public string Directory { get; set; } = Path.Combine(Path.GetTempPath(), "grimoire-cover-cache");
}

/// <summary>Outcome of a cover lookup. <see cref="CoverOutcome.NotFound"/> is a cached fact, not an error.</summary>
public enum CoverOutcome
{
    Found,
    NotFound,
    Unavailable,
}

/// <summary>Result of a cover lookup; <see cref="FilePath"/> is set only when <see cref="Outcome"/> is Found.</summary>
public sealed record CoverResult(CoverOutcome Outcome, string? FilePath);

/// <summary>
/// Resolves release-group cover art from the Cover Art Archive (feature B6, D6: free source,
/// on-disk cache, no object storage). Caches both hits and misses so a band with no cover is
/// asked for exactly once. The client never touches CAA directly — everything goes through here.
/// </summary>
public sealed class CoverArtCache
{
    // A 500px front thumbnail; CAA always serves these as JPEG (verified against real MBIDs).
    private const string ThumbnailPath = "front-500";

    private readonly HttpClient _http;
    private readonly ILogger<CoverArtCache> _logger;
    private readonly string _directory;

    public CoverArtCache(HttpClient http, IOptions<CoverCacheOptions> options, ILogger<CoverArtCache> logger)
    {
        _http = http;
        _logger = logger;
        // Absolute path so PhysicalFile() can serve straight from it.
        _directory = Path.GetFullPath(options.Value.Directory);
    }

    /// <summary>
    /// Returns the cover for a release-group MBID, fetching and caching on a miss. A 404 from CAA
    /// is remembered as a negative marker; transient failures (5xx, timeouts) are never cached, so
    /// they are retried on the next request.
    /// </summary>
    public async Task<CoverResult> GetAsync(Guid mbid, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);

        string key = mbid.ToString("D");
        string imagePath = Path.Combine(_directory, key + ".jpg");
        string missPath = Path.Combine(_directory, key + ".404");

        if (File.Exists(imagePath))
        {
            return new CoverResult(CoverOutcome.Found, imagePath);
        }

        if (File.Exists(missPath))
        {
            return new CoverResult(CoverOutcome.NotFound, null);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"release-group/{key}/{ThumbnailPath}", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Cover Art Archive request failed for {Mbid}; not caching a negative.", key);
            return new CoverResult(CoverOutcome.Unavailable, null);
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await WriteAtomicAsync(missPath, Array.Empty<byte>(), ct);
                return new CoverResult(CoverOutcome.NotFound, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                // Transient (rate limit, 5xx). Do not cache — this band may have a cover.
                _logger.LogWarning(
                    "Cover Art Archive returned {Status} for {Mbid}; not caching.",
                    (int)response.StatusCode,
                    key);
                return new CoverResult(CoverOutcome.Unavailable, null);
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct);
            await WriteAtomicAsync(imagePath, bytes, ct);
            return new CoverResult(CoverOutcome.Found, imagePath);
        }
    }

    // Write to a unique temp file then move into place, so a concurrent reader never sees a
    // half-written cache entry.
    private static async Task WriteAtomicAsync(string finalPath, byte[] bytes, CancellationToken ct)
    {
        string temp = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, ct);
        File.Move(temp, finalPath, overwrite: true);
    }
}
