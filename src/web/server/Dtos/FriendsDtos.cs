using System.ComponentModel.DataAnnotations;

namespace Grimoire.Server.Dtos;

/// <summary>
/// An accepted friend (FRIENDS wave): who they are, their public handle, how deep they have gone
/// (Depth Score, feature B15) and how many bands they have summoned, plus the friendship row's id
/// and status. Depth and count are recomputed from their live grimoire so they never drift.
/// </summary>
public record FriendDto(
    Guid UserId,
    string? Handle,
    int DepthScore,
    int SummonedCount,
    Guid FriendshipId,
    string Status);

/// <summary>A pending friend request, from either side (FRIENDS wave). <see cref="UserId"/> is the other party.</summary>
public record FriendRequestDto(
    Guid FriendshipId,
    Guid UserId,
    string? Handle,
    DateTime CreatedAt);

/// <summary>The caller's pending friend requests, split by direction (FRIENDS wave).</summary>
public record FriendRequestsDto(
    IReadOnlyList<FriendRequestDto> Incoming,
    IReadOnlyList<FriendRequestDto> Outgoing);

/// <summary>The body of "send a friend request": the handle of the user to befriend (FRIENDS wave).</summary>
public record SendFriendRequestBody(
    [Required] string Handle);

/// <summary>
/// The outcome of sending a friend request (FRIENDS wave): the friendship row's id and its resulting
/// status — "Pending" for a new request, or "Accepted" when it completed a request they had already
/// sent you (a mutual accept).
/// </summary>
public record FriendRequestResultDto(
    Guid FriendshipId,
    string Status);

/// <summary>
/// One rung of the friends leaderboard (FRIENDS wave): a user ranked by Depth Score, with a flag for
/// the caller's own row so the front can highlight "you".
/// </summary>
public record LeaderboardEntryDto(
    Guid UserId,
    string? Handle,
    int DepthScore,
    bool IsSelf);

/// <summary>
/// A friend's taste projected onto the Atlas plane (FRIENDS wave), the same plane as the star field.
/// Both coordinates are null when the friend has no taste vector or the projection cannot be built —
/// a designed empty state, never a fabricated point.
/// </summary>
public record FriendAtlasPointDto(
    double? X,
    double? Y);
