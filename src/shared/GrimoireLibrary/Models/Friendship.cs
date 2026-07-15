namespace Grimoire.Library.Models;

/// <summary>
/// A directed friendship edge between two users (the FRIENDS wave). <see cref="RequesterId"/> is
/// whoever created the row; <see cref="AddresseeId"/> is the other party. A <see cref="Status"/> of
/// <see cref="FriendshipStatus.Pending"/> is an outstanding request the addressee may accept or
/// decline; <see cref="FriendshipStatus.Accepted"/> is a mutual friendship (queried in both
/// directions); <see cref="FriendshipStatus.Blocked"/> is a one-directional wall the requester
/// raises against the addressee. At most one row per ordered (requester, addressee) pair.
/// </summary>
public class Friendship
{
    public Guid Id { get; set; }

    public Guid RequesterId { get; set; }

    public Guid AddresseeId { get; set; }

    public FriendshipStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
