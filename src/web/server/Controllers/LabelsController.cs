using Grimoire.Library.Data;
using Grimoire.Server.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Movement V — Labels as a door (B21). This is how people actually find metal: you trust a label
/// (Peaceville, Earache, Nuclear Blast, Southern Lord) and walk its roster. The index lists the
/// real labels the ETL resolved (179 of them, 299 releases carry a <c>label_id</c>); a label page
/// is its releases, each linking to the band it belongs to.
///
/// <para>
/// Coverage is partial — most releases have no label yet — so a label with nothing on it (or a
/// thin index) renders a designed empty state. Nothing is invented: a label appears only when a
/// release points at it.
/// </para>
/// </summary>
[ApiController]
[Route("api/labels")]
public class LabelsController : ControllerBase
{
    private const int MaxLabels = 200;

    private readonly GrimoireDbContext _db;

    public LabelsController(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The labels that have at least one release on them, most releases first (B21). Empty when no
    /// release carries a label yet.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LabelSummaryDto>>> List(CancellationToken ct = default)
    {
        // Count releases per label id in one grouped pass (only labels that carry at least one),
        // then join to label identity. Filtering on a projected subquery does not translate.
        Dictionary<Guid, int> counts = await _db.Releases
            .AsNoTracking()
            .Where(r => r.LabelId != null)
            .GroupBy(r => r.LabelId!.Value)
            .Select(g => new { LabelId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LabelId, x => x.Count, ct);

        if (counts.Count == 0)
        {
            return Ok(Array.Empty<LabelSummaryDto>());
        }

        List<Guid> labelIds = counts.Keys.ToList();

        List<LabelSummaryDto> labels = (await _db.Labels
                .AsNoTracking()
                .Where(l => labelIds.Contains(l.Id))
                .Select(l => new { l.Id, l.Name, l.Country })
                .ToListAsync(ct))
            .Select(l => new LabelSummaryDto(l.Id, l.Name, l.Country, counts[l.Id]))
            .OrderByDescending(l => l.ReleaseCount)
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxLabels)
            .ToList();

        return Ok(labels);
    }

    /// <summary>
    /// A label's page (B21): its releases, each with the band it belongs to (the door), newest
    /// first. 404 when the label id is unknown; an empty release list is a designed empty state.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LabelDetailDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var label = await _db.Labels
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new { l.Id, l.Name, l.Country })
            .FirstOrDefaultAsync(ct);

        if (label is null)
        {
            return NotFound();
        }

        List<LabelReleaseDto> releases = await _db.Releases
            .AsNoTracking()
            .Where(r => r.LabelId == id && r.Artist != null)
            .OrderByDescending(r => r.ReleaseDate ?? DateOnly.MinValue)
            .ThenBy(r => r.Title)
            .Select(r => new LabelReleaseDto(
                r.Id,
                r.Mbid,
                r.Title,
                r.Type,
                r.ReleaseDate,
                r.ArtistId,
                r.Artist!.Name,
                r.Artist!.Rank))
            .ToListAsync(ct);

        return Ok(new LabelDetailDto(label.Id, label.Name, label.Country, releases));
    }
}
