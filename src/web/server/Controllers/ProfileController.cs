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

        string? handle = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Handle)
            .FirstOrDefaultAsync(ct);

        ProfileDto dto = new(
            depthScore,
            summoned.Count,
            anchorCount,
            deepestCut,
            ProfileAggregates.RankBreakdown(summoned),
            ProfileAggregates.ByDecade(summoned),
            ProfileAggregates.ByCountry(summoned, TopCountries),
            ProfileAggregates.ByGenre(summoned, TopGenres),
            handle);

        return Ok(dto);
    }

    /// <summary>
    /// Claims or changes the caller's public friend handle (FRIENDS wave). 400 when the handle is
    /// malformed (not 3-30 chars of <c>[a-z0-9_]</c>); 409 when another user already holds it. The
    /// handle is stored lower-cased, so it is compared and reserved case-insensitively. 204 on success.
    /// </summary>
    [HttpPut("handle")]
    public async Task<IActionResult> SetHandle([FromBody] SetHandleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = CurrentUserId();

        string? normalized = HandleValidator.Normalize(request.Handle);

        if (normalized is null)
        {
            return BadRequest(new
            {
                message = "A handle is 3-30 characters of lowercase letters, digits or underscore (a-z, 0-9, _).",
            });
        }

        bool taken = await _db.Users
            .AnyAsync(u => u.Handle == normalized && u.Id != userId, ct);

        if (taken)
        {
            return Conflict(new { message = "That handle is already taken." });
        }

        GrimoireUser? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return NotFound(new { message = "No such user." });
        }

        user.Handle = normalized;
        await _db.SaveChangesAsync(ct);

        return NoContent();
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
    /// Re-picks taste anchors from the profile (the same act as the sign-up cold start, offered again to a
    /// signed-in user). Two modes, case-insensitive:
    /// <list type="bullet">
    /// <item><c>"fresh"</c> — REPLACE the anchor set with exactly <see cref="ReseedRequest.ArtistIds"/> (a "new
    /// account" reset), then re-seed <c>user_taste.embedding</c> with the MEAN of the picked artists' already-CENTRED
    /// embeddings (DECISIONS D26 — never re-centred) via <see cref="TasteMath.Seed"/>.</item>
    /// <item><c>"add"</c> — UNION the picks into the existing anchors (nothing removed), then re-seed the taste
    /// vector with the mean over ALL of the user's anchors that carry an embedding.</item>
    /// </list>
    /// Ids that name no real band, and duplicates, are ignored. An anchor is allowed even when its artist has no
    /// embedding — it is still recorded, it just does not move the vector (only embedding-having artists feed the
    /// mean, matching <see cref="RebuildTaste"/>). <c>Repulsion</c> is left untouched. The taste row is created if
    /// the user had none. If, after resolving the picks, NO embedding-having artist can seed the vector, the whole
    /// call is refused with a 400 and NOTHING is written — in BOTH modes, mirroring <see cref="RebuildTaste"/>'s
    /// empty case (the anchor changes are not persisted either, so a failed re-seed never leaves a user with anchors
    /// but no taste). <see cref="RebuildResultDto.DepthScore"/> is the current value (it is over summons, not anchors).
    /// </summary>
    [HttpPost("reseed")]
    public async Task<ActionResult<RebuildResultDto>> Reseed([FromBody] ReseedRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid userId = CurrentUserId();

        string? mode = request.Mode?.Trim().ToLowerInvariant();

        if (mode is not ("fresh" or "add"))
        {
            return BadRequest(new { message = "Mode must be \"fresh\" or \"add\"." });
        }

        List<Guid> ids = (request.ArtistIds ?? Array.Empty<Guid>()).Distinct().ToList();

        if (ids.Count == 0)
        {
            return BadRequest(new { message = "Pick at least one band to re-seed your taste." });
        }

        // One query: the real bands among the picked ids, with their (already-centred, D26) embeddings.
        // Ids that name no band simply do not come back, so bogus ids are ignored rather than fatal.
        var picked = await _db.Artists
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Embedding })
            .ToListAsync(ct);

        if (picked.Count == 0)
        {
            return BadRequest(new { message = "None of those ids names a real band." });
        }

        int anchorsUsed;
        float[] seed;

        if (mode == "fresh")
        {
            List<float[]> usable = picked
                .Where(p => p.Embedding != null)
                .Select(p => p.Embedding!.ToArray())
                .ToList();

            if (usable.Count == 0)
            {
                return BadRequest(new
                {
                    message = "None of the picked bands has an embedding yet; taste cannot be re-seeded from them.",
                });
            }

            seed = TasteMath.Seed(usable);
            anchorsUsed = usable.Count;

            // Make the anchor set EXACTLY the picked bands: drop what is no longer picked, add what is new.
            // (Diffing rather than delete-all + re-insert avoids EF tracking two rows with the same key.)
            HashSet<Guid> pickedIds = picked.Select(p => p.Id).ToHashSet();
            List<TasteAnchor> existing = await _db.TasteAnchors
                .Where(a => a.UserId == userId)
                .ToListAsync(ct);
            HashSet<Guid> existingIds = existing.Select(a => a.ArtistId).ToHashSet();

            foreach (TasteAnchor stale in existing.Where(a => !pickedIds.Contains(a.ArtistId)))
            {
                _db.TasteAnchors.Remove(stale);
            }

            foreach (Guid fresh in pickedIds.Where(id => !existingIds.Contains(id)))
            {
                _db.TasteAnchors.Add(new TasteAnchor
                {
                    UserId = userId,
                    ArtistId = fresh,
                    AddedAt = DateTime.UtcNow,
                });
            }
        }
        else
        {
            // "add": union the picks into the existing anchors. One query for the current anchor set joined to
            // its embeddings — gives us both which ids are already anchored and the vectors already in the mean.
            var existingAnchors = await _db.TasteAnchors
                .Where(a => a.UserId == userId)
                .Join(_db.Artists, a => a.ArtistId, artist => artist.Id, (a, artist) => new { artist.Id, artist.Embedding })
                .ToListAsync(ct);
            HashSet<Guid> existingIds = existingAnchors.Select(a => a.Id).ToHashSet();

            var newPicks = picked.Where(p => !existingIds.Contains(p.Id)).ToList();

            // The mean is over ALL post-union anchors that carry an embedding: the ones already anchored plus
            // the newly added picks (which are, by construction, disjoint from the existing set).
            List<float[]> allEmbeddings = existingAnchors
                .Where(a => a.Embedding != null)
                .Select(a => a.Embedding!.ToArray())
                .Concat(newPicks.Where(p => p.Embedding != null).Select(p => p.Embedding!.ToArray()))
                .ToList();

            if (allEmbeddings.Count == 0)
            {
                return BadRequest(new
                {
                    message = "No anchor with an embedding to re-seed from. Pick at least one band that has been embedded.",
                });
            }

            seed = TasteMath.Seed(allEmbeddings);
            anchorsUsed = allEmbeddings.Count;

            foreach (var pick in newPicks)
            {
                _db.TasteAnchors.Add(new TasteAnchor
                {
                    UserId = userId,
                    ArtistId = pick.Id,
                    AddedAt = DateTime.UtcNow,
                });
            }
        }

        UserTaste taste = await _db.UserTastes.FirstOrDefaultAsync(t => t.UserId == userId, ct)
            ?? AddTaste(userId);

        taste.Embedding = new Vector(seed);
        taste.UpdatedAt = DateTimeOffset.UtcNow;

        // Snapshot the new position on the trajectory (mirrors RebuildTaste / RiteController.AddSnapshot): a
        // fresh copy of the vector so the snapshot never shares the live taste's array.
        _db.TasteSnapshots.Add(new TasteSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Embedding = new Vector(seed),
            DepthScore = taste.DepthScore,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(ct);

        // Depth Score is over summons, not anchors — a re-seed leaves it as-is; return the current value.
        List<Rank?> summonedRanks = await _db.Rites
            .Where(r => r.UserId == userId && r.State == RiteState.Summoned)
            .Join(_db.Artists, r => r.ArtistId, a => a.Id, (r, a) => a.Rank)
            .ToListAsync(ct);

        return Ok(new RebuildResultDto(anchorsUsed, true, DepthScore.Compute(summonedRanks)));
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
