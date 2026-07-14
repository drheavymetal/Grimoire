using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// The mirror and the user's own cartography (features C20, C16, B25, B18, B23): the app turning the
/// user's rite history back on them. None of these invents data — each reads <c>rites</c>,
/// <c>user_taste</c> and <c>taste_snapshots</c>, and every view has a designed empty state for the
/// user who has not yet judged enough to reflect.
/// </summary>
[ApiController]
[Route("api/mirror")]
[Authorize]
public class MirrorController : ControllerBase
{
    private readonly GrimoireDbContext _db;
    private readonly AtlasProjector _projector;

    public MirrorController(GrimoireDbContext db, AtlasProjector projector)
    {
        _db = db;
        _projector = projector;
    }

    // -----------------------------------------------------------------------
    // C20 — the mirror
    // -----------------------------------------------------------------------

    /// <summary>
    /// The mirror (C20): what fraction of the bands the user banished blind carry their favourite
    /// genre. Proof of the app's thesis, with the user's own ear as witness. No new data — rites only.
    /// </summary>
    [HttpGet("reflection")]
    public async Task<ActionResult<MirrorDto>> Reflection(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        IReadOnlyList<string[]> summoned = await TagsForStateAsync(userId, RiteState.Summoned, ct);
        IReadOnlyList<string[]> banished = await TagsForStateAsync(userId, RiteState.Banished, ct);

        MirrorMath.MirrorResult r = MirrorMath.Compute(
            summoned.Select(t => (IReadOnlyList<string>)t).ToList(),
            banished.Select(t => (IReadOnlyList<string>)t).ToList());

        return Ok(new MirrorDto(r.HasData, r.FavouriteTag, r.BanishedTotal, r.BanishedMatching, r.Fraction));
    }

    // -----------------------------------------------------------------------
    // C16 — your trajectory
    // -----------------------------------------------------------------------

    /// <summary>
    /// The trajectory (C16): the path the taste vector travelled, one point per snapshot in time,
    /// each projected onto the Atlas plane where possible, with the drift between steps and the total.
    /// </summary>
    [HttpGet("trajectory")]
    public async Task<ActionResult<TrajectoryDto>> Trajectory(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        List<TasteSnapshot> snapshots = await _db.TasteSnapshots
            .Where(s => s.UserId == userId && s.Embedding != null)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

        List<float[]> vectors = snapshots.Select(s => s.Embedding!.ToArray()).ToList();
        double[] drift = TrajectoryMath.DriftSeries(vectors);
        double total = TrajectoryMath.TotalDrift(vectors);

        List<TrajectoryPointDto> points = [];
        for (int i = 0; i < snapshots.Count; i++)
        {
            (double X, double Y)? xy = await _projector.ProjectTasteAsync(vectors[i], ct);
            double stepDrift = i == 0 ? 0.0 : drift[i - 1];
            points.Add(new TrajectoryPointDto(
                snapshots[i].CreatedAt,
                snapshots[i].DepthScore,
                stepDrift,
                xy?.X,
                xy?.Y));
        }

        return Ok(new TrajectoryDto(points, total));
    }

    // -----------------------------------------------------------------------
    // B25 — anti-recommendation
    // -----------------------------------------------------------------------

    /// <summary>
    /// The anti-recommendation (B25): the unjudged band nearest the user's repulsion vector — "this
    /// one will repel you, and here is why". Needs a repulsion (something banished); otherwise the
    /// honest empty state.
    /// </summary>
    [HttpGet("anti-rec")]
    public async Task<ActionResult<AntiRecDto>> AntiRec(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        UserTaste? taste = await _db.UserTastes.FirstOrDefaultAsync(t => t.UserId == userId, ct);

        if (taste?.Repulsion is null)
        {
            return Ok(new AntiRecDto(false, null));
        }

        // Everything the user has already judged is off the table: an anti-rec must be a fresh band.
        IQueryable<Guid> judged = _db.Rites.Where(r => r.UserId == userId).Select(r => r.ArtistId);

        var nearest = await _db.Artists
            .Discoverable()
            .Where(a => !judged.Contains(a.Id))
            .OrderBy(a => a.Embedding!.CosineDistance(taste.Repulsion))
            .Select(a => new { a.Id, a.Name, a.Country, a.FormedYear, a.Rank, a.Tags, a.Embedding })
            .FirstOrDefaultAsync(ct);

        if (nearest is null)
        {
            return Ok(new AntiRecDto(false, null));
        }

        float[] repulsion = taste.Repulsion.ToArray();
        float[] bandVec = nearest.Embedding!.ToArray();
        double distToRepulsion = VectorMath.CosineDistance(bandVec, repulsion);
        double distToTaste = taste.Embedding is null
            ? double.NaN
            : VectorMath.CosineDistance(bandVec, taste.Embedding.ToArray());

        // Which of the anti-rec's tags overlap the genres the user actually rejected.
        HashSet<string> rejectedTags = (await TagsForStateAsync(userId, RiteState.Banished, ct))
            .SelectMany(t => t)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> sharedRejected = nearest.Tags.Where(rejectedTags.Contains).ToList();

        AntiRecBandDto band = new(
            nearest.Id,
            nearest.Name,
            nearest.Country,
            nearest.FormedYear,
            nearest.Rank,
            nearest.Tags,
            distToRepulsion,
            distToTaste,
            sharedRejected);

        return Ok(new AntiRecDto(true, band));
    }

    // -----------------------------------------------------------------------
    // B18 — Dark Twin
    // -----------------------------------------------------------------------

    /// <summary>
    /// The Dark Twin (B18): the user whose taste is closest to yours yet whose collection is most
    /// disjoint. Anonymous. With too few users, the honest empty state.
    /// </summary>
    [HttpGet("dark-twin")]
    public async Task<ActionResult<DarkTwinDto>> DarkTwin(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        UserTaste? me = await _db.UserTastes.FirstOrDefaultAsync(t => t.UserId == userId, ct);

        if (me?.Embedding is null)
        {
            return Ok(new DarkTwinDto(false, 0, 0, 0, []));
        }

        HashSet<Guid> mySummoned = (await _db.Rites
                .Where(r => r.UserId == userId && r.State == RiteState.Summoned)
                .Select(r => r.ArtistId)
                .ToListAsync(ct))
            .ToHashSet();

        // Other users with a taste vector, and their summoned sets.
        List<(Guid UserId, float[] Taste)> others = (await _db.UserTastes
                .Where(t => t.UserId != userId && t.Embedding != null)
                .Select(t => new { t.UserId, t.Embedding })
                .ToListAsync(ct))
            .Select(t => (t.UserId, t.Embedding!.ToArray()))
            .ToList();

        if (others.Count == 0)
        {
            return Ok(new DarkTwinDto(false, 0, 0, 0, []));
        }

        List<Guid> otherIds = others.Select(o => o.UserId).ToList();
        Dictionary<Guid, HashSet<Guid>> summonedByUser = (await _db.Rites
                .Where(r => otherIds.Contains(r.UserId) && r.State == RiteState.Summoned)
                .Select(r => new { r.UserId, r.ArtistId })
                .ToListAsync(ct))
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ArtistId).ToHashSet());

        List<DarkTwinMath.Candidate> candidates = others
            .Select(o => new DarkTwinMath.Candidate(
                o.UserId,
                o.Taste,
                summonedByUser.TryGetValue(o.UserId, out HashSet<Guid>? s) ? s : []))
            .ToList();

        DarkTwinMath.TwinResult? twin = DarkTwinMath.Best(me.Embedding.ToArray(), mySummoned, candidates);

        if (twin is null)
        {
            return Ok(new DarkTwinDto(false, 0, 0, 0, []));
        }

        HashSet<Guid> theirSummoned = summonedByUser.TryGetValue(twin.Value.UserId, out HashSet<Guid>? ts)
            ? ts
            : [];
        List<Guid> theirsOnly = theirSummoned.Where(id => !mySummoned.Contains(id)).ToList();
        int sharedCount = theirSummoned.Count(mySummoned.Contains);

        List<ArtistSummaryDto> theirsOnlyBands = await _db.Artists
            .AsNoTracking()
            .Where(a => theirsOnly.Contains(a.Id))
            .OrderBy(a => a.Name)
            .Select(a => new ArtistSummaryDto(a.Id, a.Name, a.Country, a.FormedYear, a.Rank))
            .ToListAsync(ct);

        return Ok(new DarkTwinDto(
            true,
            twin.Value.TasteSimilarity,
            twin.Value.Disjointness,
            sharedCount,
            theirsOnlyBands));
    }

    // -----------------------------------------------------------------------
    // B23 — gaps
    // -----------------------------------------------------------------------

    /// <summary>
    /// The gaps (B23): the decades, countries and subgenres of the catalogue the user has never
    /// summoned — the dark regions of the Atlas, biggest first.
    /// </summary>
    [HttpGet("gaps")]
    public async Task<ActionResult<GapsDto>> Gaps([FromQuery] int limit = 12, CancellationToken ct = default)
    {
        Guid userId = CurrentUserId();
        int take = Math.Clamp(limit, 1, 50);

        HashSet<Guid> summoned = (await _db.Rites
                .Where(r => r.UserId == userId && r.State == RiteState.Summoned)
                .Select(r => r.ArtistId)
                .ToListAsync(ct))
            .ToHashSet();

        // Pull the coarse facets of the whole catalogue once (small: a few thousand rows).
        var facets = await _db.Artists
            .AsNoTracking()
            .Select(a => new { a.Id, a.FormedYear, a.Country, a.Tags })
            .ToListAsync(ct);

        Dictionary<string, int> touchedDecades = new();
        HashSet<string> touchedCountries = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> touchedTags = new(StringComparer.OrdinalIgnoreCase);
        foreach (var a in facets.Where(a => summoned.Contains(a.Id)))
        {
            if (a.FormedYear is int y)
            {
                touchedDecades[DecadeLabel(y)] = 1;
            }

            if (!string.IsNullOrWhiteSpace(a.Country))
            {
                touchedCountries.Add(a.Country);
            }

            foreach (string tag in a.Tags)
            {
                touchedTags.Add(tag);
            }
        }

        List<GapBucketDto> decades = facets
            .Where(a => a.FormedYear != null)
            .GroupBy(a => DecadeLabel(a.FormedYear!.Value))
            .Where(g => !touchedDecades.ContainsKey(g.Key))
            .Select(g => new GapBucketDto(g.Key, g.Count()))
            .OrderByDescending(b => b.CatalogueCount)
            .ThenBy(b => b.Label)
            .Take(take)
            .ToList();

        List<GapBucketDto> countries = facets
            .Where(a => !string.IsNullOrWhiteSpace(a.Country))
            .GroupBy(a => a.Country!)
            .Where(g => !touchedCountries.Contains(g.Key))
            .Select(g => new GapBucketDto(g.Key, g.Count()))
            .OrderByDescending(b => b.CatalogueCount)
            .ThenBy(b => b.Label)
            .Take(take)
            .ToList();

        List<GapBucketDto> subgenres = facets
            .SelectMany(a => a.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Where(g => !touchedTags.Contains(g.Key))
            .Select(g => new GapBucketDto(g.Key, g.Count()))
            .OrderByDescending(b => b.CatalogueCount)
            .ThenBy(b => b.Label, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();

        return Ok(new GapsDto(decades, countries, subgenres));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>The tag arrays of the bands the user has rites for in a given state.</summary>
    private async Task<IReadOnlyList<string[]>> TagsForStateAsync(Guid userId, RiteState state, CancellationToken ct)
    {
        return await _db.Rites
            .Where(r => r.UserId == userId && r.State == state)
            .Join(_db.Artists, r => r.ArtistId, a => a.Id, (r, a) => a.Tags)
            .ToListAsync(ct);
    }

    private static string DecadeLabel(int year)
    {
        int decade = (year / 10) * 10;
        return $"{decade}s";
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
