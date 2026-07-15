namespace Grimoire.Library.Models;

/// <summary>
/// An in-app notification in a user's inbox (the NOTIFICATIONS wave). Polled, not pushed: the front
/// reads a paged list and an unread count, and marks rows read. <see cref="UserId"/> is the
/// recipient; <see cref="ActorId"/> is whoever caused it (the requester, the accepter, the gifter),
/// null when no actor applies. <see cref="PayloadJson"/> holds the type-specific fields as JSON:
/// <c>{ friendshipId }</c> for a friend request, <c>{ }</c> for an accept, and
/// <c>{ giftToken, artistName }</c> for a received gift. <see cref="ReadAt"/> is null until read.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }

    /// <summary>The recipient — whose inbox this lands in.</summary>
    public Guid UserId { get; set; }

    public NotificationType Type { get; set; }

    /// <summary>Whoever caused the notification (requester/accepter/gifter). Null when no actor applies.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>Type-specific fields serialised as JSON (jsonb). Null when the type carries no payload.</summary>
    public string? PayloadJson { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When the recipient read it. Null while unread — the unread count filters on this.</summary>
    public DateTime? ReadAt { get; set; }
}
