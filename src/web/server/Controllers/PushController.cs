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
/// Web Push subscription management for the Weekly Rite (feature B17). The public VAPID key is
/// public (the front needs it to subscribe); subscribing and unsubscribing require a signed-in user
/// and persist the browser's own capability handle in <c>push_subscriptions</c>. The server never
/// mints subscriptions — <c>PushManager.subscribe</c> in the browser does — it only stores them.
/// </summary>
[ApiController]
[Route("api/push")]
[Authorize]
public class PushController : ControllerBase
{
    private readonly GrimoireDbContext _db;
    private readonly WebPushSender _sender;

    public PushController(GrimoireDbContext db, WebPushSender sender)
    {
        _db = db;
        _sender = sender;
    }

    /// <summary>
    /// The VAPID public key the front feeds to <c>PushManager.subscribe</c>. Anonymous: it is public
    /// key material by design. Returns 503 when no key is configured, so the UI can hide push cleanly.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("vapid-public-key")]
    public ActionResult<VapidKeyDto> VapidPublicKey()
    {
        if (string.IsNullOrWhiteSpace(_sender.PublicKey))
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "Web Push is not configured: no VAPID public key is set." });
        }

        return Ok(new VapidKeyDto(_sender.PublicKey));
    }

    /// <summary>
    /// Stores (or refreshes) the caller's push subscription. Upserts on the endpoint, so a browser
    /// re-subscribing does not pile up rows and can be re-owned by whoever is signed in.
    /// </summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(PushSubscribeRequest request, CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        PushSubscription? existing = await _db.PushSubscriptions
            .FirstOrDefaultAsync(p => p.Endpoint == request.Endpoint, ct);

        if (existing is null)
        {
            _db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.UserId = userId;
            existing.P256dh = request.P256dh;
            existing.Auth = request.Auth;
        }

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Removes the caller's subscription for an endpoint (the browser unsubscribed).</summary>
    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe(PushSubscribeRequest request, CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        await _db.PushSubscriptions
            .Where(p => p.UserId == userId && p.Endpoint == request.Endpoint)
            .ExecuteDeleteAsync(ct);

        return NoContent();
    }

    private Guid CurrentUserId()
    {
        string? sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(sub, out Guid id))
        {
            return id;
        }

        throw new InvalidOperationException("Authenticated request carries no usable subject claim.");
    }
}
