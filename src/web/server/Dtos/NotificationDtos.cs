using System.ComponentModel.DataAnnotations;

namespace Grimoire.Server.Dtos;

/// <summary>
/// One notification in a user's inbox (the NOTIFICATIONS wave), with its payload flattened into the
/// nullable fields the type carries: <see cref="FriendshipId"/> for a friend request,
/// <see cref="GiftToken"/> and <see cref="ArtistName"/> for a received gift (C22),
/// <see cref="GameId"/> plus <see cref="ScoreCorrect"/>/<see cref="ScoreTotal"/> for a played
/// verdict game (the GAMES wave), none for an accept. <see cref="ActorHandle"/> is the actor's
/// public handle, looked up from their user row. <see cref="Read"/> is true once the recipient has
/// marked it read.
/// </summary>
public record NotificationDto(
    Guid Id,
    string Type,
    Guid? ActorId,
    string? ActorHandle,
    DateTime CreatedAt,
    bool Read,
    Guid? FriendshipId,
    string? GiftToken,
    string? ArtistName,
    Guid? GameId,
    int? ScoreCorrect,
    int? ScoreTotal);

/// <summary>The unread notification count (the NOTIFICATIONS wave), for the inbox badge.</summary>
public record UnreadCountDto(int Count);

/// <summary>The outcome of "mark all read" (the NOTIFICATIONS wave): how many rows were marked.</summary>
public record MarkedReadDto(int Marked);

/// <summary>The body of "gift a band to a friend" (the NOTIFICATIONS wave): which band to send (C22).</summary>
public record GiftToFriendRequest(
    [Required] Guid ArtistId);
