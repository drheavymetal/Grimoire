using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Services;

/// <summary>
/// The one answer to "may these two see each other's things?" (the FRIENDS wave, D57). Extracted so
/// every caller asks the same question the same way: this is an authorisation rule, and an
/// authorisation rule with two copies is an authorisation rule with two answers the day one of them
/// is edited. Everything friend-scoped — a grimoire, a crossed grimoire, an Atlas point, a gift, a
/// face-off, a game — goes through here.
/// </summary>
public sealed class FriendshipGuard
{
    private readonly GrimoireDbContext _db;

    public FriendshipGuard(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Whether two users are ACCEPTED friends, in either direction. Pending is not friendship (the
    /// request was never answered) and Blocked is its opposite, so only Accepted passes.
    /// </summary>
    public async Task<bool> AreAcceptedFriendsAsync(Guid a, Guid b, CancellationToken ct)
    {
        return await _db.Friendships.AnyAsync(
            f => f.Status == FriendshipStatus.Accepted
                && ((f.RequesterId == a && f.AddresseeId == b) || (f.RequesterId == b && f.AddresseeId == a)),
            ct);
    }
}
