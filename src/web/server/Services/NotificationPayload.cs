using System.Text.Json;
using Grimoire.Library.Models;

namespace Grimoire.Server.Services;

/// <summary>
/// The type-specific body of a notification (the NOTIFICATIONS wave), serialised into
/// <see cref="Notification.PayloadJson"/> and flattened back out for the DTO. Isolated here so the
/// round trip is unit-tested without a database: each <see cref="NotificationType"/> owns a payload
/// shape, and <see cref="Flatten"/> reads whichever fields that type carries, tolerating a null or
/// malformed body by returning empty fields rather than throwing.
/// </summary>
public static class NotificationPayload
{
    /// <summary>A friend request: the friendship row the recipient may accept or decline.</summary>
    public record FriendRequest(Guid FriendshipId);

    /// <summary>A received gift (C22): the opaque capability token and the band's name to show.</summary>
    public record GiftReceived(string GiftToken, string ArtistName);

    /// <summary>
    /// A friend finished guessing your verdicts (the GAMES wave): the game they played and how they
    /// scored. This notification IS the turn hand-off — it carries the result and invites the reply,
    /// which is how the game stays turn-based without anything realtime (D60).
    /// </summary>
    public record VerdictGamePlayed(Guid GameId, int Correct, int Total);

    /// <summary>The payload fields flattened for a <see cref="Dtos.NotificationDto"/> — all nullable, present by type.</summary>
    public record Flattened(
        Guid? FriendshipId,
        string? GiftToken,
        string? ArtistName,
        Guid? GameId,
        int? ScoreCorrect,
        int? ScoreTotal)
    {
        public static readonly Flattened Empty = new(null, null, null, null, null, null);
    }

    /// <summary>Serialises a payload object to JSON, or null when there is nothing to carry.</summary>
    public static string? Serialize(object? payload)
    {
        return payload is null ? null : JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// Reads the fields a given notification type carries out of its JSON body. A null or malformed
    /// body yields <see cref="Flattened.Empty"/> — an honest gap, never a throw on read.
    /// </summary>
    public static Flattened Flatten(NotificationType type, string? payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson))
        {
            return Flattened.Empty;
        }

        try
        {
            switch (type)
            {
                case NotificationType.FriendRequest:
                    FriendRequest? request = JsonSerializer.Deserialize<FriendRequest>(payloadJson);
                    return request is null
                        ? Flattened.Empty
                        : new Flattened(request.FriendshipId, null, null, null, null, null);

                case NotificationType.GiftReceived:
                    GiftReceived? gift = JsonSerializer.Deserialize<GiftReceived>(payloadJson);
                    return gift is null
                        ? Flattened.Empty
                        : new Flattened(null, gift.GiftToken, gift.ArtistName, null, null, null);

                case NotificationType.VerdictGamePlayed:
                    VerdictGamePlayed? played = JsonSerializer.Deserialize<VerdictGamePlayed>(payloadJson);
                    return played is null
                        ? Flattened.Empty
                        : new Flattened(null, null, null, played.GameId, played.Correct, played.Total);

                default:
                    return Flattened.Empty;
            }
        }
        catch (JsonException)
        {
            return Flattened.Empty;
        }
    }
}
