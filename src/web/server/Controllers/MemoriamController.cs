using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// In Memoriam (feature C12): the musicians in the grimoire who have died, in chronological order.
/// Death dates and places come from Wikidata (P570/P20), populated by the deaths ETL; only people
/// Wikidata asserts have died appear, so the list is real and never invented. The tone is plain by
/// design — a record of who they were and what they played, not a spectacle.
/// </summary>
[ApiController]
[Route("api/memoriam")]
public class MemoriamController : ControllerBase
{
    private readonly GrimoireDbContext _db;

    public MemoriamController(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The remembered musicians, earliest death first, each with the bands they played in. Empty
    /// when no death is on record — a quiet, honest empty state rather than a fabricated list.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MemoriamEntryDto>>> List(CancellationToken ct = default)
    {
        var people = await _db.Artists
            .AsNoTracking()
            .Where(a => a.DeathDate != null)
            .OrderBy(a => a.DeathDate)
            .ThenBy(a => a.Name)
            .Select(a => new { a.Id, a.Name, DeathDate = a.DeathDate!.Value, a.DeathPlace })
            .ToListAsync(ct);

        if (people.Count == 0)
        {
            return Ok(Array.Empty<MemoriamEntryDto>());
        }

        List<Guid> personIds = people.Select(p => p.Id).ToList();

        // The membership edges touching these people; the band is the counterpart of the person.
        var edges = await _db.ArtistEdges
            .AsNoTracking()
            .Where(e => e.Kind == EdgeKind.MemberOf
                && (personIds.Contains(e.FromId) || personIds.Contains(e.ToId)))
            .Select(e => new { e.FromId, e.ToId })
            .ToListAsync(ct);

        // Resolve the band identities for every counterpart in one query.
        var personIdSet = personIds.ToHashSet();
        List<Guid> bandIds = edges
            .Select(e => personIdSet.Contains(e.FromId) ? e.ToId : e.FromId)
            .Distinct()
            .ToList();

        Dictionary<Guid, MemoriamBandDto> bands = await _db.Artists
            .AsNoTracking()
            .Where(a => bandIds.Contains(a.Id))
            .Select(a => new MemoriamBandDto(a.Id, a.Name, a.Rank))
            .ToDictionaryAsync(b => b.Id, b => b, ct);

        // Group the bands by person.
        var bandsByPerson = new Dictionary<Guid, List<MemoriamBandDto>>();
        foreach (var e in edges)
        {
            Guid personId = personIdSet.Contains(e.FromId) ? e.FromId : e.ToId;
            Guid bandId = personIdSet.Contains(e.FromId) ? e.ToId : e.FromId;

            if (!bands.TryGetValue(bandId, out MemoriamBandDto? band))
            {
                continue;
            }

            if (!bandsByPerson.TryGetValue(personId, out List<MemoriamBandDto>? list))
            {
                list = [];
                bandsByPerson[personId] = list;
            }

            if (!list.Any(b => b.Id == band.Id))
            {
                list.Add(band);
            }
        }

        List<MemoriamEntryDto> result = people
            .Select(p => new MemoriamEntryDto(
                p.Id,
                p.Name,
                p.DeathDate,
                p.DeathPlace,
                bandsByPerson.TryGetValue(p.Id, out List<MemoriamBandDto>? list)
                    ? list.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList()
                    : []))
            .ToList();

        return Ok(result);
    }
}
