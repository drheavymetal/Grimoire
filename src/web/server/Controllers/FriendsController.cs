using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Friends (the FRIENDS wave): request/accept/decline/remove/block, the friends list and pending
/// requests, a Depth Score leaderboard, and — for accepted friends only — reading their grimoire,
/// crossing grimoires (feature C23, shared with the by-code path) and placing their taste on the
/// Atlas (feature C18/B22, shared with <c>AtlasController</c>). Every endpoint requires a signed-in
/// user and reads only relationships the caller is part of.
/// </summary>
[ApiController]
[Route("api/friends")]
[Authorize]
public class FriendsController : ControllerBase
{
    private readonly GrimoireDbContext _db;
    private readonly GrimoireCrossService _cross;
    private readonly AtlasProjector _projector;
    private readonly NotificationService _notifications;
    private readonly FriendshipGuard _guard;
    private readonly IDataProtector _giftProtector;

    public FriendsController(
        GrimoireDbContext db,
        GrimoireCrossService cross,
        AtlasProjector projector,
        NotificationService notifications,
        FriendshipGuard guard,
        IDataProtectionProvider protection)
    {
        _db = db;
        _cross = cross;
        _projector = projector;
        _notifications = notifications;
        _guard = guard;
        _giftProtector = protection.CreateProtector(GiftToken.Purpose);
    }

    // -----------------------------------------------------------------------
    // The friends list, requests and leaderboard
    // -----------------------------------------------------------------------

    /// <summary>The caller's accepted friends (both directions), with each friend's Depth Score and summon count.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FriendDto>>> Friends(CancellationToken ct)
    {
        Guid me = CurrentUserId();

        List<Friendship> accepted = await _db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted && (f.RequesterId == me || f.AddresseeId == me))
            .ToListAsync(ct);

        List<Guid> friendIds = accepted
            .Select(f => f.RequesterId == me ? f.AddresseeId : f.RequesterId)
            .ToList();

        Dictionary<Guid, string?> handles = await HandlesAsync(friendIds, ct);
        Dictionary<Guid, (int Depth, int Count)> stats = await DepthStatsAsync(friendIds, ct);

        List<FriendDto> friends = accepted
            .Select(f =>
            {
                Guid friendId = f.RequesterId == me ? f.AddresseeId : f.RequesterId;
                (int depth, int count) = stats.TryGetValue(friendId, out (int Depth, int Count) s) ? s : (0, 0);
                handles.TryGetValue(friendId, out string? handle);

                return new FriendDto(friendId, handle, depth, count, f.Id, f.Status.ToString());
            })
            .OrderByDescending(f => f.DepthScore)
            .ToList();

        return Ok(friends);
    }

    /// <summary>The caller's pending friend requests, split into incoming (to accept) and outgoing (awaiting).</summary>
    [HttpGet("requests")]
    public async Task<ActionResult<FriendRequestsDto>> Requests(CancellationToken ct)
    {
        Guid me = CurrentUserId();

        List<Friendship> pending = await _db.Friendships
            .Where(f => f.Status == FriendshipStatus.Pending && (f.RequesterId == me || f.AddresseeId == me))
            .ToListAsync(ct);

        List<Guid> others = pending
            .Select(f => f.RequesterId == me ? f.AddresseeId : f.RequesterId)
            .ToList();

        Dictionary<Guid, string?> handles = await HandlesAsync(others, ct);

        List<FriendRequestDto> incoming = pending
            .Where(f => f.AddresseeId == me)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FriendRequestDto(f.Id, f.RequesterId, Handle(handles, f.RequesterId), f.CreatedAt))
            .ToList();

        List<FriendRequestDto> outgoing = pending
            .Where(f => f.RequesterId == me)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FriendRequestDto(f.Id, f.AddresseeId, Handle(handles, f.AddresseeId), f.CreatedAt))
            .ToList();

        return Ok(new FriendRequestsDto(incoming, outgoing));
    }

    /// <summary>
    /// Sends a friend request by handle. 404 for an unknown handle; 400 for befriending yourself;
    /// 409 when a block or an existing friendship/request is in the way. If the addressee had already
    /// sent the caller a pending request, this ACCEPTS it instead (a mutual accept) and returns Accepted.
    /// </summary>
    [HttpPost("request")]
    public async Task<ActionResult<FriendRequestResultDto>> SendRequest([FromBody] SendFriendRequestBody body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Guid me = CurrentUserId();

        string? handle = HandleValidator.Normalize(body.Handle);

        if (handle is null)
        {
            return NotFound(new { message = "No user answers to that handle." });
        }

        Guid? targetId = await _db.Users
            .Where(u => u.Handle == handle)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        if (targetId is null)
        {
            return NotFound(new { message = "No user answers to that handle." });
        }

        Guid target = targetId.Value;

        if (target == me)
        {
            return BadRequest(new { message = "You cannot send yourself a friend request." });
        }

        List<Friendship> edges = await EdgesBetweenAsync(me, target, ct);

        if (edges.Any(f => f.Status == FriendshipStatus.Blocked))
        {
            return Conflict(new { message = "This user cannot be added." });
        }

        if (edges.Any(f => f.Status == FriendshipStatus.Accepted))
        {
            return Conflict(new { message = "You are already friends." });
        }

        // They already asked you: complete it as a mutual accept rather than a second pending row.
        Friendship? reversePending = edges.FirstOrDefault(f =>
            f.Status == FriendshipStatus.Pending && f.RequesterId == target);

        if (reversePending is not null)
        {
            reversePending.Status = FriendshipStatus.Accepted;
            reversePending.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // This completes a request THEY sent me: it reads as an accept, so notify the original
            // requester (target) that I (me) accepted — same shape as the explicit accept below.
            await _notifications.CreateAsync(target, NotificationType.FriendAccepted, me, null, ct);

            return Ok(new FriendRequestResultDto(reversePending.Id, reversePending.Status.ToString()));
        }

        if (edges.Any(f => f.Status == FriendshipStatus.Pending && f.RequesterId == me))
        {
            return Conflict(new { message = "You have already sent this user a request." });
        }

        DateTime now = DateTime.UtcNow;
        Friendship friendship = new()
        {
            Id = Guid.NewGuid(),
            RequesterId = me,
            AddresseeId = target,
            Status = FriendshipStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync(ct);

        // Tell the addressee I asked; the payload carries the friendship id they may accept/decline.
        await _notifications.CreateAsync(
            target,
            NotificationType.FriendRequest,
            me,
            new NotificationPayload.FriendRequest(friendship.Id),
            ct);

        return Ok(new FriendRequestResultDto(friendship.Id, friendship.Status.ToString()));
    }

    /// <summary>Accepts a pending request. Only the addressee may accept. 404 unknown; 403 not addressee; 409 not pending.</summary>
    [HttpPost("{friendshipId:guid}/accept")]
    public async Task<IActionResult> Accept(Guid friendshipId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        Friendship? friendship = await _db.Friendships.FirstOrDefaultAsync(f => f.Id == friendshipId, ct);

        if (friendship is null)
        {
            return NotFound(new { message = "No such friend request." });
        }

        if (friendship.AddresseeId != me)
        {
            return Forbid();
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            return Conflict(new { message = "This request is no longer pending." });
        }

        friendship.Status = FriendshipStatus.Accepted;
        friendship.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Tell the original requester that I (the addressee) accepted. No payload — the actor is enough.
        await _notifications.CreateAsync(friendship.RequesterId, NotificationType.FriendAccepted, me, null, ct);

        return NoContent();
    }

    /// <summary>Declines a pending request (deletes it). Only the addressee may decline. 404/403/409 as accept.</summary>
    [HttpPost("{friendshipId:guid}/decline")]
    public async Task<IActionResult> Decline(Guid friendshipId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        Friendship? friendship = await _db.Friendships.FirstOrDefaultAsync(f => f.Id == friendshipId, ct);

        if (friendship is null)
        {
            return NotFound(new { message = "No such friend request." });
        }

        if (friendship.AddresseeId != me)
        {
            return Forbid();
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            return Conflict(new { message = "This request is no longer pending." });
        }

        _db.Friendships.Remove(friendship);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Removes an accepted friendship (either party). 404 unknown; 403 not a party; 409 if not accepted.</summary>
    [HttpDelete("{friendshipId:guid}")]
    public async Task<IActionResult> Remove(Guid friendshipId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        Friendship? friendship = await _db.Friendships.FirstOrDefaultAsync(f => f.Id == friendshipId, ct);

        if (friendship is null)
        {
            return NotFound(new { message = "No such friendship." });
        }

        if (friendship.RequesterId != me && friendship.AddresseeId != me)
        {
            return Forbid();
        }

        if (friendship.Status != FriendshipStatus.Accepted)
        {
            return Conflict(new { message = "That friendship is not active." });
        }

        _db.Friendships.Remove(friendship);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Blocks a user: raises (or replaces) a one-directional Blocked wall Me→them and clears any
    /// pending/accepted edges between the two (their own block of the caller, if any, is left intact).
    /// A blocked user can no longer send the caller requests. 400 for blocking yourself. 204.
    /// </summary>
    [HttpPost("{userId:guid}/block")]
    public async Task<IActionResult> Block(Guid userId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        if (userId == me)
        {
            return BadRequest(new { message = "You cannot block yourself." });
        }

        List<Friendship> edges = await EdgesBetweenAsync(me, userId, ct);

        // Drop the reverse pending/accepted edges (never their Blocked wall against me) and my own
        // pending/accepted edge, leaving a single Blocked row Me→them.
        List<Friendship> reverseToClear = edges
            .Where(f => f.RequesterId == userId && f.Status != FriendshipStatus.Blocked)
            .ToList();

        if (reverseToClear.Count > 0)
        {
            _db.Friendships.RemoveRange(reverseToClear);
        }

        DateTime now = DateTime.UtcNow;
        Friendship? forward = edges.FirstOrDefault(f => f.RequesterId == me);

        if (forward is not null)
        {
            forward.Status = FriendshipStatus.Blocked;
            forward.UpdatedAt = now;
        }
        else
        {
            _db.Friendships.Add(new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = me,
                AddresseeId = userId,
                Status = FriendshipStatus.Blocked,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Lifts the caller's block of a user (deletes the Blocked wall Me→them). Idempotent 204.</summary>
    [HttpDelete("{userId:guid}/block")]
    public async Task<IActionResult> Unblock(Guid userId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        Friendship? block = await _db.Friendships.FirstOrDefaultAsync(
            f => f.RequesterId == me && f.AddresseeId == userId && f.Status == FriendshipStatus.Blocked, ct);

        if (block is not null)
        {
            _db.Friendships.Remove(block);
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    /// <summary>The caller and their accepted friends, ranked by Depth Score (feature B15), highest first.</summary>
    [HttpGet("leaderboard")]
    public async Task<ActionResult<IReadOnlyList<LeaderboardEntryDto>>> Leaderboard(CancellationToken ct)
    {
        Guid me = CurrentUserId();

        List<Friendship> accepted = await _db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted && (f.RequesterId == me || f.AddresseeId == me))
            .ToListAsync(ct);

        List<Guid> ids = accepted
            .Select(f => f.RequesterId == me ? f.AddresseeId : f.RequesterId)
            .Append(me)
            .Distinct()
            .ToList();

        Dictionary<Guid, string?> handles = await HandlesAsync(ids, ct);
        Dictionary<Guid, (int Depth, int Count)> stats = await DepthStatsAsync(ids, ct);

        List<LeaderboardEntryDto> board = ids
            .Select(id => new LeaderboardEntryDto(
                id,
                Handle(handles, id),
                stats.TryGetValue(id, out (int Depth, int Count) s) ? s.Depth : 0,
                id == me))
            .OrderByDescending(e => e.DepthScore)
            .ThenBy(e => e.Handle, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(board);
    }

    // -----------------------------------------------------------------------
    // A friend's grimoire, crossed grimoires and Atlas point (accepted friends only)
    // -----------------------------------------------------------------------

    /// <summary>A friend's summoned bands, newest first — same shape as the caller's own grimoire. 403 if not friends.</summary>
    [HttpGet("{friendId:guid}/grimoire")]
    public async Task<ActionResult<IReadOnlyList<GrimoireEntryDto>>> FriendGrimoire(Guid friendId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        if (!await AreAcceptedFriendsAsync(me, friendId, ct))
        {
            return Forbid();
        }

        List<GrimoireEntryDto> entries = await _db.Rites
            .Where(r => r.UserId == friendId && r.State == RiteState.Summoned)
            .OrderByDescending(r => r.ResolvedAt)
            .Join(
                _db.Artists,
                r => r.ArtistId,
                a => a.Id,
                (r, a) => new GrimoireEntryDto(
                    new ArtistSummaryDto(a.Id, a.Name, a.Country, a.FormedYear, a.Rank),
                    r.ResolvedAt!.Value))
            .ToListAsync(ct);

        return Ok(entries);
    }

    /// <summary>Crosses the caller's grimoire with a friend's (feature C23). 403 if not friends.</summary>
    [HttpGet("{friendId:guid}/crossed")]
    public async Task<ActionResult<CrossedGrimoiresDto>> Crossed(Guid friendId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        if (!await AreAcceptedFriendsAsync(me, friendId, ct))
        {
            return Forbid();
        }

        return Ok(await _cross.CrossAsync(me, friendId, ct));
    }

    /// <summary>
    /// A friend's taste placed on the Atlas plane (feature C18/B22), the same projector the caller's
    /// own "you are here" marker uses. Coordinates are null when the friend has no taste or the
    /// projection cannot be built. 403 if not friends.
    /// </summary>
    [HttpGet("{friendId:guid}/atlas-point")]
    public async Task<ActionResult<FriendAtlasPointDto>> AtlasPoint(Guid friendId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        if (!await AreAcceptedFriendsAsync(me, friendId, ct))
        {
            return Forbid();
        }

        float[]? taste = (await _db.UserTastes
                .AsNoTracking()
                .Where(t => t.UserId == friendId)
                .Select(t => t.Embedding)
                .FirstOrDefaultAsync(ct))
            ?.ToArray();

        if (taste is null)
        {
            return Ok(new FriendAtlasPointDto(null, null));
        }

        (double X, double Y)? projected = await _projector.ProjectTasteAsync(taste, ct);

        return Ok(projected is null
            ? new FriendAtlasPointDto(null, null)
            : new FriendAtlasPointDto(projected.Value.X, projected.Value.Y));
    }

    /// <summary>
    /// Gifts a band to an accepted friend (C22, the NOTIFICATIONS wave): wraps the band into an
    /// opaque gift token — the same encrypted capability the by-link gift uses — and drops a
    /// GiftReceived notification into the friend's inbox. 403 if not accepted friends; 404 if the
    /// band is unknown. 204.
    /// </summary>
    [HttpPost("{friendId:guid}/gift")]
    public async Task<IActionResult> Gift(Guid friendId, [FromBody] GiftToFriendRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Guid me = CurrentUserId();

        if (!await AreAcceptedFriendsAsync(me, friendId, ct))
        {
            return Forbid();
        }

        var artist = await _db.Artists
            .AsNoTracking()
            .Where(a => a.Id == body.ArtistId)
            .Select(a => new { a.Id, a.Name })
            .FirstOrDefaultAsync(ct);

        if (artist is null)
        {
            return NotFound(new { message = "That band is not in the grimoire." });
        }

        // Same wrapping as GiftController: the band id is sealed inside the token, never a db row.
        string token = GiftToken.Wrap(_giftProtector, new GiftToken.Payload(artist.Id, null));

        await _notifications.CreateAsync(
            friendId,
            NotificationType.GiftReceived,
            me,
            new NotificationPayload.GiftReceived(token, artist.Name),
            ct);

        return NoContent();
    }

    // -----------------------------------------------------------------------
    // The taste face-off (light, async — accepted friends only)
    // -----------------------------------------------------------------------

    /// <summary>
    /// A light taste face-off against a friend (FRIENDS wave): each user's Depth Score and who is
    /// deeper, the grimoire cross (shared / mine-only / theirs-only counts, feature C23) and the
    /// alignment of the two tastes. Async and read-only — no realtime, no new table; the friend opens
    /// the same view for themselves. 403 if not accepted friends.
    /// </summary>
    [HttpGet("{friendId:guid}/duel")]
    public async Task<ActionResult<DuelFaceOffDto>> Duel(Guid friendId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        if (!await AreAcceptedFriendsAsync(me, friendId, ct))
        {
            return Forbid();
        }

        Dictionary<Guid, (int Depth, int Count)> stats = await DepthStatsAsync([me, friendId], ct);
        int myDepth = stats.TryGetValue(me, out (int Depth, int Count) mine) ? mine.Depth : 0;
        int theirDepth = stats.TryGetValue(friendId, out (int Depth, int Count) theirs) ? theirs.Depth : 0;

        string winner = myDepth > theirDepth ? "me" : theirDepth > myDepth ? "them" : "tie";

        // Grimoire cross (C23): reuse the one implementation and reduce its three lists to counts.
        CrossedGrimoiresDto cross = await _cross.CrossAsync(me, friendId, ct);

        double? alignment = await AlignmentAsync(me, friendId, ct);

        return Ok(new DuelFaceOffDto(
            myDepth,
            theirDepth,
            winner,
            cross.Shared.Count,
            cross.YoursOnly.Count,
            cross.TheirsOnly.Count,
            alignment));
    }

    /// <summary>
    /// Challenges a friend to a taste face-off (FRIENDS wave): drops a <see cref="NotificationType.DuelChallenge"/>
    /// notification into their inbox (actor = the caller). No realtime and no state — the friend opens
    /// the same face-off view themselves. 403 if not accepted friends. 204.
    /// </summary>
    [HttpPost("{friendId:guid}/duel/challenge")]
    public async Task<IActionResult> ChallengeDuel(Guid friendId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        if (!await AreAcceptedFriendsAsync(me, friendId, ct))
        {
            return Forbid();
        }

        await _notifications.CreateAsync(friendId, NotificationType.DuelChallenge, me, new { }, ct);

        return NoContent();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>All friendship rows between two users, in either direction (at most a handful).</summary>
    private async Task<List<Friendship>> EdgesBetweenAsync(Guid a, Guid b, CancellationToken ct)
    {
        return await _db.Friendships
            .Where(f => (f.RequesterId == a && f.AddresseeId == b) || (f.RequesterId == b && f.AddresseeId == a))
            .ToListAsync(ct);
    }

    /// <summary>Delegates to the shared guard: one definition of "accepted friends" for every caller.</summary>
    private async Task<bool> AreAcceptedFriendsAsync(Guid a, Guid b, CancellationToken ct)
    {
        return await _guard.AreAcceptedFriendsAsync(a, b, ct);
    }

    /// <summary>Public handles for a set of user ids (missing ids simply absent from the map).</summary>
    private async Task<Dictionary<Guid, string?>> HandlesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Handle })
            .ToDictionaryAsync(u => u.Id, u => u.Handle, ct);
    }

    /// <summary>
    /// Depth Score and summon count for a set of users, computed from their live grimoires in one
    /// query (feature B15). A user with no summons is simply absent — the caller reads it as (0, 0).
    /// </summary>
    private async Task<Dictionary<Guid, (int Depth, int Count)>> DepthStatsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await _db.Rites
            .Where(r => ids.Contains(r.UserId) && r.State == RiteState.Summoned)
            .Join(_db.Artists, r => r.ArtistId, a => a.Id, (r, a) => new { r.UserId, a.Rank })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => (DepthScore.Compute(g.Select(r => r.Rank)), g.Count()));
    }

    /// <summary>
    /// The alignment of two users' tastes for the face-off: the cosine similarity (0..1) of their
    /// stored taste vectors, read in a single query. Both vectors are already centred (DECISIONS D26)
    /// so they are compared as-is — never re-centred. Null when either user has no taste vector yet
    /// (an honest gap, never a fabricated zero). The similarity is clamped to [0, 1] to match the
    /// contract's range (a rare anti-correlation floors at 0 rather than going negative).
    /// </summary>
    private async Task<double?> AlignmentAsync(Guid me, Guid friendId, CancellationToken ct)
    {
        Dictionary<Guid, float[]> vectors = (await _db.UserTastes
                .AsNoTracking()
                .Where(t => (t.UserId == me || t.UserId == friendId) && t.Embedding != null)
                .Select(t => new { t.UserId, t.Embedding })
                .ToListAsync(ct))
            .ToDictionary(t => t.UserId, t => t.Embedding!.ToArray());

        if (!vectors.TryGetValue(me, out float[]? mine) || !vectors.TryGetValue(friendId, out float[]? theirs))
        {
            return null;
        }

        double similarity = 1.0 - VectorMath.CosineDistance(mine, theirs);

        return Math.Clamp(similarity, 0.0, 1.0);
    }

    private static string? Handle(IReadOnlyDictionary<Guid, string?> handles, Guid id)
    {
        return handles.TryGetValue(id, out string? handle) ? handle : null;
    }

    private Guid CurrentUserId()
    {
        // MapInboundClaims is off, so the subject is the raw "sub" claim.
        string? sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(sub, out Guid id))
        {
            return id;
        }

        throw new InvalidOperationException("Authenticated request carries no usable subject claim.");
    }
}
