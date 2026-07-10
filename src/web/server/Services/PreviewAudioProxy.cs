namespace Grimoire.Server.Services;

/// <summary>
/// Streams a 30–45 s preview from iTunes/Deezer through the server so the origin URL never
/// reaches the browser (SPEC §5.3, feature B13). Without this proxy, devtools would show the
/// preview URL — which usually embeds the band name — and the blind mechanic would die in ten
/// seconds.
///
/// <para>
/// SSRF is closed off two ways: the URL is <b>never</b> taken from the client — it is always the
/// <c>preview_url</c> our own ETL resolved and stored on the artist — and, defence in depth, the
/// host must be on <see cref="AllowedHosts"/>. A stored URL that somehow points elsewhere is
/// refused rather than fetched.
/// </para>
/// </summary>
public sealed class PreviewAudioProxy
{
    /// <summary>The only hosts a stored preview URL is allowed to point at.</summary>
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        // iTunes preview CDN (the primary source, DECISIONS D25).
        "audio-ssl.itunes.apple.com",
        "audio-preview.itunes.apple.com",
        "mzstatic.com",
        // Deezer preview CDN (the complement).
        "cdns-preview-a.dzcdn.net",
        "cdns-preview-b.dzcdn.net",
        "cdns-preview-c.dzcdn.net",
        "cdns-preview-d.dzcdn.net",
        "cdns-preview-e.dzcdn.net",
        "cdns-preview-f.dzcdn.net",
        "cdnt-preview.dzcdn.net",
    };

    private readonly HttpClient _http;
    private readonly ILogger<PreviewAudioProxy> _logger;

    public PreviewAudioProxy(HttpClient http, ILogger<PreviewAudioProxy> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// True when <paramref name="previewUrl"/> is an absolute https URL on an allowed host. A
    /// stored URL that fails this is treated as no audio, never fetched.
    /// </summary>
    public static bool IsAllowed(string? previewUrl)
    {
        if (!Uri.TryCreate(previewUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // Allow an exact host or any subdomain of an allowed apex (e.g. *.mzstatic.com).
        return AllowedHosts.Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Opens the upstream preview for streaming (response headers read, body left on the wire).
    /// Returns null when the URL is not allowed or the upstream did not return success; the
    /// caller must dispose the message. The origin URL is never surfaced to the client.
    /// </summary>
    public async Task<HttpResponseMessage?> OpenAsync(string previewUrl, CancellationToken ct)
    {
        if (!IsAllowed(previewUrl))
        {
            _logger.LogWarning("Refusing to proxy a preview URL on a disallowed host.");
            return null;
        }

        HttpResponseMessage response = await _http.GetAsync(
            previewUrl,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Upstream preview returned {Status}.", (int)response.StatusCode);
            response.Dispose();
            return null;
        }

        return response;
    }
}
