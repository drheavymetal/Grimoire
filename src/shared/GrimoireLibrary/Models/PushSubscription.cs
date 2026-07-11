namespace Grimoire.Library.Models;

/// <summary>
/// A Web Push subscription owned by a user (feature B17, Weekly Rite delivery). It is the
/// browser's own capability handle: <see cref="Endpoint"/> is the push service URL the server
/// POSTs to, and <see cref="P256dh"/>/<see cref="Auth"/> are the client public key and auth
/// secret used to encrypt the payload (RFC 8291). The server never mints these — the browser's
/// <c>PushManager.subscribe</c> does, and the front hands them over. One row per browser endpoint.
///
/// <para>
/// EXPOSURE (declared, DECISIONS D28 style): the endpoint is a bearer capability. Anyone holding
/// it could push to that browser <em>if</em> they also had the VAPID private key, which lives only
/// in user-secrets and is never committed or returned. The rows are deleted on account cascade;
/// a stale endpoint (browser unsubscribed) is pruned lazily when a send returns 404/410.
/// </para>
/// </summary>
public class PushSubscription
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The push service URL to POST the encrypted notification to. Unique per browser.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The client's ECDH public key (base64url), for payload encryption.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>The client's auth secret (base64url), for payload encryption.</summary>
    public string Auth { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
