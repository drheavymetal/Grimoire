using System.ComponentModel.DataAnnotations;

namespace Grimoire.Server.Dtos;

/// <summary>The VAPID public key the front hands to <c>PushManager.subscribe</c> (feature B17).</summary>
public record VapidKeyDto(string PublicKey);

/// <summary>
/// A browser's push subscription, flattened from the <c>PushSubscription.toJSON()</c> the front
/// obtains from <c>PushManager.subscribe</c>. The server never mints these; the browser does.
/// </summary>
public record PushSubscribeRequest(
    [Required] string Endpoint,
    [Required] string P256dh,
    [Required] string Auth);
