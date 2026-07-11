using WebPush;

namespace Grimoire.Server.Services;

/// <summary>VAPID configuration for Web Push (feature B17). Bound from the "WebPush" section.</summary>
public sealed class WebPushOptions
{
    /// <summary>The VAPID subject: a <c>mailto:</c> or <c>https:</c> contact the push service can reach.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The VAPID public key (base64url). Safe to expose — the front needs it to subscribe.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// The VAPID private key (base64url). NEVER committed — it lives only in user-secrets (dev) or an
    /// environment variable (prod). Without it, sending is disabled and <c>notify</c> reports the gap.
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;
}

/// <summary>
/// The outcome of a push send: the endpoint became gone (404/410, so it should be pruned), it was
/// delivered to the push service, or it failed for another reason (logged, not pruned).
/// </summary>
public enum PushSendResult
{
    Delivered,
    Gone,
    Failed,
}

/// <summary>
/// Sends encrypted Web Push notifications (feature B17 delivery) via the <c>WebPush</c> library,
/// which does the RFC 8291 payload encryption and RFC 8292 VAPID signing. This is real plumbing,
/// not a stub: it POSTs to the browser's push service. It is <see cref="Enabled"/> only when a VAPID
/// private key is configured (blocker/exposure D28-style: the key is a secret, never committed).
///
/// <para>
/// Verifiability limit (declared): actually observing a notification pop from the OS needs a real
/// browser subscription and a real push service, neither of which exists in a headless environment.
/// The encryption, signing and HTTP call are exercised; end-to-end OS delivery is not.
/// </para>
/// </summary>
public sealed class WebPushSender
{
    private readonly WebPushOptions _options;
    private readonly ILogger<WebPushSender> _logger;
    private readonly WebPushClient _client;

    public WebPushSender(WebPushOptions options, ILogger<WebPushSender> logger)
    {
        _options = options;
        _logger = logger;
        _client = new WebPushClient();
    }

    /// <summary>Whether sending is configured (a VAPID key pair is present). If false, notify → 503.</summary>
    public bool Enabled =>
        !string.IsNullOrWhiteSpace(_options.PublicKey)
        && !string.IsNullOrWhiteSpace(_options.PrivateKey)
        && !string.IsNullOrWhiteSpace(_options.Subject);

    /// <summary>The public VAPID key the front hands to <c>PushManager.subscribe</c>.</summary>
    public string PublicKey => _options.PublicKey;

    /// <summary>
    /// Encrypts <paramref name="payloadJson"/> for the subscription and POSTs it to the push service.
    /// Returns <see cref="PushSendResult.Gone"/> for a dead endpoint (404/410) so the caller can prune
    /// it, and <see cref="PushSendResult.Failed"/> for anything else (logged, kept). Never throws.
    /// </summary>
    public async Task<PushSendResult> SendAsync(
        string endpoint,
        string p256dh,
        string auth,
        string payloadJson,
        CancellationToken ct)
    {
        if (!Enabled)
        {
            return PushSendResult.Failed;
        }

        PushSubscription subscription = new(endpoint, p256dh, auth);
        VapidDetails vapid = new(_options.Subject, _options.PublicKey, _options.PrivateKey);

        try
        {
            await _client.SendNotificationAsync(subscription, payloadJson, vapid, ct);
            return PushSendResult.Delivered;
        }
        catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound
                                          or System.Net.HttpStatusCode.Gone)
        {
            // The browser unsubscribed or the endpoint expired: tell the caller to prune it.
            _logger.LogInformation("Push endpoint is gone ({Status}); it will be pruned.", ex.StatusCode);
            return PushSendResult.Gone;
        }
        catch (WebPushException ex)
        {
            _logger.LogWarning(ex, "Web push send failed with status {Status}.", ex.StatusCode);
            return PushSendResult.Failed;
        }
        catch (HttpRequestException ex)
        {
            // The push service was unreachable (offline/headless environment); logged, not fatal.
            _logger.LogWarning(ex, "Web push send could not reach the push service.");
            return PushSendResult.Failed;
        }
        catch (Exception ex)
        {
            // A malformed subscription (bad key material) can throw during RFC 8291 encryption. One
            // bad subscription must not 500 the request or abort the batch; log it and move on.
            _logger.LogWarning(ex, "Web push send failed to encrypt or dispatch a notification.");
            return PushSendResult.Failed;
        }
    }
}
