using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Artist search and detail (features B1, B4, B5). Search uses PostgreSQL trigram
/// similarity (pg_trgm) against a GIN index; nothing here is faked.
/// </summary>
[ApiController]
[Route("api/artists")]
public class ArtistsController : ControllerBase
{
    private readonly GrimoireDbContext _db;

    public ArtistsController(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Fuzzy artist search by name, ordered by trigram similarity. Uses the `%`
    /// operator (GIN trigram index) to filter and `similarity()` to rank.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ArtistSummaryDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<ArtistSummaryDto>());
        }

        int take = Math.Clamp(limit, 1, 100);
        string term = q.Trim();

        List<ArtistSummaryDto> results = await _db.Artists
            .Where(a => EF.Functions.TrigramsAreSimilar(a.Name, term))
            .OrderByDescending(a => EF.Functions.TrigramsSimilarity(a.Name, term))
            .ThenBy(a => a.Name)
            .Take(take)
            .Select(a => new ArtistSummaryDto(a.Id, a.Name, a.Country, a.FormedYear, a.Rank))
            .ToListAsync(ct);

        return Ok(results);
    }

    /// <summary>Full artist detail: identity, tags, releases and bloodline edges.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ArtistDetailDto>> GetById(Guid id, CancellationToken ct = default)
    {
        Artist? artist = await _db.Artists
            .AsNoTracking()
            .Include(a => a.Releases)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (artist is null)
        {
            return NotFound();
        }

        List<ArtistEdgeDto> edges = await _db.ArtistEdges
            .AsNoTracking()
            .Where(e => e.FromId == id || e.ToId == id)
            .Select(e => new ArtistEdgeDto(e.FromId, e.ToId, e.Kind, e.BeginDate, e.EndDate, e.Instruments))
            .ToListAsync(ct);

        List<ReleaseDto> releases = artist.Releases
            .OrderBy(r => r.ReleaseDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.Title)
            .Select(r => new ReleaseDto(r.Id, r.Title, r.Type, r.ReleaseDate, r.CoverUrl))
            .ToList();

        ArtistDetailDto dto = new(
            artist.Id,
            artist.Name,
            artist.SortName,
            artist.Kind,
            artist.Country,
            artist.City,
            artist.FormedYear,
            artist.DissolvedYear,
            artist.Listeners,
            artist.Rank,
            artist.Tags,
            artist.Abstract,
            artist.ImageUrl,
            artist.Links,
            releases,
            edges);

        return Ok(dto);
    }
}
