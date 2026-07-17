using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// The in-app notification inbox (the NOTIFICATIONS wave): a polled list, an unread count, and the
/// two ways to clear it. Not web push — the front polls these endpoints. Every endpoint acts only on
/// the caller's own notifications (recipient = the signed-in user). Rows are written by
/// <see cref="NotificationService"/> from the friend and gift flows; here they are only read and
/// marked read.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private const int MaxTake = 100;

    private readonly GrimoireDbContext _db;

    public NotificationsController(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>The caller's notifications, newest first, paged. Each payload is flattened by type.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 30,
        CancellationToken ct = default)
    {
        Guid me = CurrentUserId();

        int safeSkip = Math.Max(0, skip);
        int safeTake = Math.Clamp(take, 1, MaxTake);

        List<Notification> rows = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == me)
            .OrderByDescending(n => n.CreatedAt)
            .Skip(safeSkip)
            .Take(safeTake)
            .ToListAsync(ct);

        // One batched lookup for the actor handles on this page, rather than a round trip per row.
        List<Guid> actorIds = rows
            .Where(n => n.ActorId is not null)
            .Select(n => n.ActorId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string?> handles = actorIds.Count == 0
            ? []
            : await _db.Users
                .Where(u => actorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Handle })
                .ToDictionaryAsync(u => u.Id, u => u.Handle, ct);

        List<NotificationDto> notifications = rows
            .Select(n =>
            {
                NotificationPayload.Flattened payload = NotificationPayload.Flatten(n.Type, n.PayloadJson);
                string? actorHandle = n.ActorId is not null && handles.TryGetValue(n.ActorId.Value, out string? h)
                    ? h
                    : null;

                return new NotificationDto(
                    n.Id,
                    n.Type.ToString(),
                    n.ActorId,
                    actorHandle,
                    n.CreatedAt,
                    n.ReadAt is not null,
                    payload.FriendshipId,
                    payload.GiftToken,
                    payload.ArtistName,
                    payload.GameId,
                    payload.ScoreCorrect,
                    payload.ScoreTotal);
            })
            .ToList();

        return Ok(notifications);
    }

    /// <summary>How many of the caller's notifications are unread (read_at IS NULL), for the badge.</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount(CancellationToken ct)
    {
        Guid me = CurrentUserId();

        int count = await _db.Notifications
            .CountAsync(n => n.UserId == me && n.ReadAt == null, ct);

        return Ok(new UnreadCountDto(count));
    }

    /// <summary>Marks one of the caller's notifications read. 204 either way — a no-op if absent or foreign.</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        Notification? notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == me, ct);

        if (notification is not null && notification.ReadAt is null)
        {
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    /// <summary>Marks all of the caller's unread notifications read, returning how many were marked.</summary>
    [HttpPost("read-all")]
    public async Task<ActionResult<MarkedReadDto>> MarkAllRead(CancellationToken ct)
    {
        Guid me = CurrentUserId();

        int marked = await _db.Notifications
            .Where(n => n.UserId == me && n.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);

        return Ok(new MarkedReadDto(marked));
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
