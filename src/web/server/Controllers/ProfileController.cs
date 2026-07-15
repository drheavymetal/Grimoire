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
using Pgvector;

namespace Grimoire.Server.Controllers;

/// <summary>
/// The user profile: a portrait of a grimoire (aggregates over the summoned bands), the editable
/// taste-anchor set, the "rebuild taste from anchors" action, and a full JSON export. The HYBRID
/// taste model (Pedro's choice) — the Rite keeps its EMA learning UNCHANGED; anchors are a separate,
/// explicit seed the user curates, and only <see cref="RebuildTaste"/> re-seeds the taste vector from
/// them. Every endpoint requires a signed-in user; each reads only the caller's own data.
/// </summary>
[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    /// <summary>How many countries the profile portrait shows before it stops (top-N).</summary>
    private const int TopCountries = 12;

    /// <summary>How many genre tags the profile portrait shows before it stops (top-N).</summary>
    private const int TopGenres = 12;

    private readonly GrimoireDbContext _db;

    public ProfileController(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The profile portrait: depth score, counts, the deepest cut, and breakdowns by rarity, decade,
    /// country and genre — all derived from the caller's summoned bands, plus the anchor-set size. An
    /// empty grimoire yields zeroes, a null deepest cut and empty lists (nothing invented).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ProfileDto>> Get(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        List<SummonedBand> summoned = await SummonedBandsAsync(userId, ct);

        int anchorCount = await _db.TasteAnchors.CountAsync(a => a.UserId == userId, ct);

        // Depth Score is the sum of rarity points over the summoned bands (feature B15) — recomputed
        // here from the live set so it never drifts from the persisted user_taste value.
        int depthScore = DepthScore.Compute(summoned.Select(b => b.Rank));

        SummonedBand? deepest = ProfileAggregates.DeepestCut(summoned);
        BandCardDto? deepestCut = deepest is null
            ? null
            : new BandCardDto(deepest.Id, deepest.Name, deepest.Rank, deepest.Country, deepest.Kind);

        ProfileDto dto = new(
            depthScore,
            summoned.Count,
            anchorCount,
            deepestCut,
            ProfileAggregates.RankBreakdown(summoned),
            ProfileAggregates.ByDecade(summoned),
            ProfileAggregates.ByCountry(summoned, TopCountries),
            ProfileAggregates.ByGenre(summoned, TopGenres));

        return Ok(dto);
    }

    /// <summary>The caller's taste anchors as band cards, newest first.</summary>
    [HttpGet("anchors")]
    public async Task<ActionResult<IReadOnlyList<BandCardDto>>> Anchors(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        List<BandCardDto> anchors = await _db.TasteAnchors
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AddedAt)
            .Join(
                _db.Artists,
                a => a.ArtistId,
                artist => artist.Id,
                (a, artist) => new BandCardDto(artist.Id, artist.Name, artist.Rank, artist.Country, artist.Kind))
            .ToListAsync(ct);

        return Ok(anchors);
    }

    /// <summary>
    /// Pins a band as a taste anchor. Idempotent: pinning a band already anchored is a no-op 204.
    /// 404 when the band does not exist.
    /// </summary>
    [HttpPost("anchors")]
    public async Task<IActionResult> AddAnchor([FromBody] AddAnchorRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = CurrentUserId();

        if (!await _db.Artists.AnyAsync(a => a.Id == request.ArtistId, ct))
        {
            return NotFound(new { message = "No band answers to that id." });
        }

        bool alreadyAnchored = await _db.TasteAnchors
            .AnyAsync(a => a.UserId == userId && a.ArtistId == request.ArtistId, ct);

        if (!alreadyAnchored)
        {
            _db.TasteAnchors.Add(new TasteAnchor
            {
                UserId = userId,
                ArtistId = request.ArtistId,
                AddedAt = DateTime.UtcNow,
            });

            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    /// <summary>Unpins a taste anchor. A no-op 204 when the band was not anchored.</summary>
    [HttpDelete("anchors/{artistId:guid}")]
    public async Task<IActionResult> RemoveAnchor(Guid artistId, CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        TasteAnchor? anchor = await _db.TasteAnchors
            .FirstOrDefaultAsync(a => a.UserId == userId && a.ArtistId == artistId, ct);

        if (anchor is not null)
        {
            _db.TasteAnchors.Remove(anchor);
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
    }

    /// <summary>
    /// Rebuilds the taste vector from the anchor set: re-seeds <c>user_taste.embedding</c> with the
    /// MEAN of the anchors' already-CENTRED embeddings (DECISIONS D26 — never re-centred) via
    /// <see cref="TasteMath.Seed"/>, and writes a taste snapshot for the trajectory (feature C16).
    /// Anchors without an embedding are skipped. Repulsion is left untouched. With no usable anchors,
    /// nothing is written and the call is a 400.
    /// </summary>
    [HttpPost("rebuild-taste")]
    public async Task<ActionResult<RebuildResultDto>> RebuildTaste(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        List<Vector> embeddings = await _db.TasteAnchors
            .Where(a => a.UserId == userId)
            .Join(_db.Artists, a => a.ArtistId, artist => artist.Id, (a, artist) => artist.Embedding)
            .Where(e => e != null)
            .Select(e => e!)
            .ToListAsync(ct);

        if (embeddings.Count == 0)
        {
            return BadRequest(new
            {
                message = "No anchors with an embedding to rebuild from. Pin at least one band that has been embedded.",
            });
        }

        List<float[]> centred = embeddings.Select(e => e.ToArray()).ToList();
        float[] seed = TasteMath.Seed(centred);

        UserTaste taste = await _db.UserTastes.FirstOrDefaultAsync(t => t.UserId == userId, ct)
            ?? AddTaste(userId);

        taste.Embedding = new Vector(seed);
        taste.UpdatedAt = DateTimeOffset.UtcNow;

        // Snapshot the new position on the trajectory (mirrors RiteController.AddSnapshot): a fresh
        // copy of the vector so the snapshot never shares the live taste's array.
        _db.TasteSnapshots.Add(new TasteSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Embedding = new Vector(seed),
            DepthScore = taste.DepthScore,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct);

        // Depth Score is over summons, not anchors — a rebuild leaves it as-is; return the current value.
        List<Rank?> summonedRanks = await _db.Rites
            .Where(r => r.UserId == userId && r.State == RiteState.Summoned)
            .Join(_db.Artists, r => r.ArtistId, a => a.Id, (r, a) => a.Rank)
            .ToListAsync(ct);

        return Ok(new RebuildResultDto(centred.Count, true, DepthScore.Compute(summonedRanks)));
    }

    /// <summary>
    /// The caller's full grimoire as a JSON file download (same shape as <c>GET /api/rite/grimoire</c>):
    /// the summoned bands, newest first, so a user can take their discoveries with them.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        List<GrimoireEntryDto> entries = await _db.Rites
            .Where(r => r.UserId == userId && r.State == RiteState.Summoned)
            .OrderByDescending(r => r.ResolvedAt)
            .Join(
                _db.Artists,
                r => r.ArtistId,
                a => a.Id,
                (r, a) => new GrimoireEntryDto(
                    new ArtistSummaryDto(a.Id, a.Name, a.Country, a.FormedYear, a.Rank),
                    r.ResolvedAt!.Value))
            .ToListAsync(ct);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(entries, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        return File(json, "application/json", "grimoire-export.json");
    }

    private UserTaste AddTaste(Guid userId)
    {
        UserTaste row = new() { UserId = userId, UpdatedAt = DateTimeOffset.UtcNow };
        _db.UserTastes.Add(row);
        return row;
    }

    private async Task<List<SummonedBand>> SummonedBandsAsync(Guid userId, CancellationToken ct)
    {
        return await _db.Rites
            .Where(r => r.UserId == userId && r.State == RiteState.Summoned)
            .Join(
                _db.Artists,
                r => r.ArtistId,
                a => a.Id,
                (r, a) => new SummonedBand(
                    a.Id,
                    a.Name,
                    a.Rank,
                    a.Country,
                    a.Kind,
                    a.FormedYear,
                    a.Listeners,
                    a.Tags))
            .ToListAsync(ct);
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
