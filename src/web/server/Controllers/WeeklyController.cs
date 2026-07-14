using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// The Weekly Rite (feature B17): seven blind bands per ISO week, the same seven for everyone that
/// week, delivered by Web Push. The seven are chosen deterministically from the servable pool
/// (<see cref="WeeklyRiteSelector"/>); each is materialised as a blind rite the user plays and
/// resolves through the ordinary audio proxy and resolve endpoints (SPEC §5.3).
/// </summary>
[ApiController]
[Route("api/weekly")]
[Authorize]
public class WeeklyController : ControllerBase
{
    private readonly GrimoireDbContext _db;
    private readonly WebPushSender _sender;
    private readonly ILogger<WeeklyController> _logger;

    public WeeklyController(GrimoireDbContext db, WebPushSender sender, ILogger<WeeklyController> logger)
    {
        _db = db;
        _sender = sender;
        _logger = logger;
    }

    /// <summary>
    /// The current week's seven, served blind. Requires a taste (409 → run cold start first). The
    /// seven are stable within the week: repeat calls reuse the rites already served this week and
    /// only mint the missing ones, so "same week → same seven" holds all week.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<WeeklyRiteDto>> Get(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        UserTaste? taste = await _db.UserTastes.FirstOrDefaultAsync(t => t.UserId == userId, ct);
        if (taste?.Embedding is null)
        {
            return Conflict(new { message = "No taste yet. Seed it before the Weekly Rite (cold start)." });
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string weekKey = WeeklyRiteSelector.IsoWeekKey(now);
        DateTimeOffset weekStart = WeekStart(now);

        // The servable pool (DECISIONS D25): discoverable (an embedding AND a discography — see
        // DiscoverableArtists; a session drummer is not a band) and audible. The seven come out of it.
        List<Guid> pool = await _db.Artists
            .Discoverable()
            .Where(a => a.PreviewUrl != null)
            .Select(a => a.Id)
            .ToListAsync(ct);

        IReadOnlyList<Guid> selected = WeeklyRiteSelector.Select(pool, weekKey);

        if (selected.Count == 0)
        {
            return Ok(new WeeklyRiteDto(weekKey, []));
        }

        // Reuse any rite already served to this user this week for a selected band (idempotency),
        // and remember the newest per artist so a re-fetch returns the same tokens.
        List<Rite> thisWeek = await _db.Rites
            .Where(r => r.UserId == userId && selected.Contains(r.ArtistId) && r.ServedAt >= weekStart)
            .ToListAsync(ct);

        Dictionary<Guid, Rite> existing = thisWeek
            .GroupBy(r => r.ArtistId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.ServedAt).First());

        // Distances to taste, so an honest per-band risk can rank the seven from nearest to farthest.
        float[] tasteVec = taste.Embedding.ToArray();
        Dictionary<Guid, double> distances = await DistancesToTasteAsync(selected, tasteVec, ct);
        Dictionary<Guid, double> risk = RiskWithinWeek(selected, distances);

        List<Rite> created = [];
        foreach (Guid artistId in selected)
        {
            if (existing.ContainsKey(artistId))
            {
                continue;
            }

            Rite rite = new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ArtistId = artistId,
                State = RiteState.Served,
                Risk = (float)risk.GetValueOrDefault(artistId, 0.5),
                ServedAt = now,
            };
            _db.Rites.Add(rite);
            created.Add(rite);
            existing[artistId] = rite;
        }

        if (created.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        List<WeeklyItemDto> items = selected
            .Select(artistId => existing[artistId])
            .Select(rite => new WeeklyItemDto(
                rite.Id,
                rite.Risk,
                $"{Request.Scheme}://{Request.Host}/api/rite/{rite.Id}/audio",
                rite.State,
                rite.State != RiteState.Served))
            .ToList();

        return Ok(new WeeklyRiteDto(weekKey, items));
    }

    /// <summary>
    /// Sends the caller a Web Push notification for the current Weekly Rite (feature B17 delivery).
    /// This is the manual/test trigger. Returns 503 when Web Push is not configured (no VAPID key),
    /// otherwise the per-subscription tally. Dead endpoints (404/410) are pruned as a side effect.
    ///
    /// <para>
    /// Verifiability limit (declared): the encryption, VAPID signing and HTTP POST all run, but a
    /// real notification popping from the OS needs a real browser subscription and a reachable push
    /// service — neither exists headless. With no subscriptions, this reports Sent=0 honestly.
    /// </para>
    /// </summary>
    [HttpPost("notify")]
    public async Task<ActionResult<NotifyResultDto>> Notify(CancellationToken ct)
    {
        if (!_sender.Enabled)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "Web Push is unavailable: no VAPID key pair is configured (private key lives in user-secrets)." });
        }

        Guid userId = CurrentUserId();

        List<PushSubscription> subs = await _db.PushSubscriptions
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

        // The SW localises from this data payload (navigator.language), so the OS notification is
        // bilingual without shipping i18next into the worker.
        string payload = JsonSerializer.Serialize(new
        {
            type = "weekly",
            count = WeeklyRiteSelector.WeeklyCount,
            url = "/weekly",
        });

        int sent = 0;
        int failed = 0;
        List<string> gone = [];

        foreach (PushSubscription sub in subs)
        {
            PushSendResult result = await _sender.SendAsync(sub.Endpoint, sub.P256dh, sub.Auth, payload, ct);
            switch (result)
            {
                case PushSendResult.Delivered:
                    sent++;
                    break;
                case PushSendResult.Gone:
                    gone.Add(sub.Endpoint);
                    break;
                default:
                    failed++;
                    break;
            }
        }

        if (gone.Count > 0)
        {
            await _db.PushSubscriptions
                .Where(p => p.UserId == userId && gone.Contains(p.Endpoint))
                .ExecuteDeleteAsync(ct);
            _logger.LogInformation("Pruned {Count} dead push endpoints for the user.", gone.Count);
        }

        return Ok(new NotifyResultDto(sent, gone.Count, failed));
    }

    /// <summary>The cosine distance from each selected band's embedding to the taste vector.</summary>
    private async Task<Dictionary<Guid, double>> DistancesToTasteAsync(
        IReadOnlyList<Guid> ids,
        float[] taste,
        CancellationToken ct)
    {
        var rows = await _db.Artists
            .Where(a => ids.Contains(a.Id) && a.Embedding != null)
            .Select(a => new { a.Id, a.Embedding })
            .ToListAsync(ct);

        return rows.ToDictionary(
            r => r.Id,
            r => VectorMath.CosineDistance(taste, r.Embedding!.ToArray()));
    }

    /// <summary>
    /// An honest risk for each of the week's bands: its rank within the seven by distance to taste,
    /// normalised to [0, 1] (nearest = 0, farthest = 1). Bands without a distance sort last.
    /// </summary>
    private static Dictionary<Guid, double> RiskWithinWeek(
        IReadOnlyList<Guid> selected,
        IReadOnlyDictionary<Guid, double> distances)
    {
        List<Guid> ordered = selected
            .OrderBy(id => distances.TryGetValue(id, out double d) ? d : double.MaxValue)
            .ToList();

        Dictionary<Guid, double> risk = new();
        int n = ordered.Count;
        for (int i = 0; i < n; i++)
        {
            risk[ordered[i]] = n <= 1 ? 0.5 : (double)i / (n - 1);
        }

        return risk;
    }

    /// <summary>Midnight (UTC) of the Monday that opens the ISO week containing <paramref name="instant"/>.</summary>
    private static DateTimeOffset WeekStart(DateTimeOffset instant)
    {
        DateTime utc = instant.UtcDateTime;
        int year = ISOWeek.GetYear(utc);
        int week = ISOWeek.GetWeekOfYear(utc);
        DateTime monday = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
        return new DateTimeOffset(DateTime.SpecifyKind(monday, DateTimeKind.Utc));
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
