using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Movement V — compare two bands (B24). Three independent signals laid side by side: the tags they
/// share (and the Jaccard overlap), the cosine distance between their embeddings (their sound), and
/// the members they have in common. The tag arithmetic is <see cref="CompareMath"/> (tested pure);
/// the distance is cosine over the already-centred embeddings (D26 — never re-centred).
///
/// <para>
/// Nothing is invented: if either band has no embedding, the distance is null and the front says so
/// rather than showing a fabricated number; no shared members is a real, common answer, not an error.
/// </para>
/// </summary>
[ApiController]
[Route("api/compare")]
public class CompareController : ControllerBase
{
    private readonly GrimoireDbContext _db;

    public CompareController(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>Compares the two bands <paramref name="a"/> and <paramref name="b"/> (B24).</summary>
    [HttpGet]
    public async Task<ActionResult<CompareResultDto>> Compare(
        [FromQuery] Guid a,
        [FromQuery] Guid b,
        CancellationToken ct = default)
    {
        if (a == Guid.Empty || b == Guid.Empty || a == b)
        {
            return BadRequest(new { message = "Two distinct band ids are required." });
        }

        var rows = await _db.Artists
            .AsNoTracking()
            .Where(x => x.Id == a || x.Id == b)
            .Select(x => new { x.Id, x.Name, x.Rank, x.Country, x.Tags, x.Embedding })
            .ToListAsync(ct);

        var rowA = rows.FirstOrDefault(r => r.Id == a);
        var rowB = rows.FirstOrDefault(r => r.Id == b);

        if (rowA is null || rowB is null)
        {
            return NotFound(new { message = "One or both of the bands are unknown." });
        }

        IReadOnlyList<string> sharedTags = CompareMath.SharedTags(rowA.Tags, rowB.Tags);
        double similarity = CompareMath.TagJaccard(rowA.Tags, rowB.Tags);

        double? distance = rowA.Embedding is not null && rowB.Embedding is not null
            ? VectorMath.CosineDistance(rowA.Embedding.ToArray(), rowB.Embedding.ToArray())
            : null;

        // Members shared by both bands: persons who are MemberOf a AND MemberOf b.
        List<Guid> membersOfA = await _db.ArtistEdges
            .AsNoTracking()
            .Where(e => e.Kind == EdgeKind.MemberOf && e.ToId == a)
            .Select(e => e.FromId)
            .ToListAsync(ct);

        HashSet<Guid> sharedMemberIds = (await _db.ArtistEdges
                .AsNoTracking()
                .Where(e => e.Kind == EdgeKind.MemberOf && e.ToId == b && membersOfA.Contains(e.FromId))
                .Select(e => e.FromId)
                .ToListAsync(ct))
            .ToHashSet();

        List<SharedMemberDto> sharedMembers = sharedMemberIds.Count == 0
            ? []
            : await _db.Artists
                .AsNoTracking()
                .Where(x => sharedMemberIds.Contains(x.Id))
                .OrderBy(x => x.Name)
                .Select(x => new SharedMemberDto(x.Id, x.Name))
                .ToListAsync(ct);

        return Ok(new CompareResultDto(
            new CompareBandDto(rowA.Id, rowA.Name, rowA.Rank, rowA.Country, rowA.Tags),
            new CompareBandDto(rowB.Id, rowB.Name, rowB.Rank, rowB.Country, rowB.Tags),
            sharedTags,
            similarity,
            distance,
            sharedMembers));
    }
}
