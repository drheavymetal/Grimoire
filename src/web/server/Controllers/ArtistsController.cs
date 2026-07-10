using Grimoire.Library.Data;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
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
    private readonly ArtistDetailBuilder _details;

    public ArtistsController(GrimoireDbContext db, ArtistDetailBuilder details)
    {
        _db = db;
        _details = details;
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
        ArtistDetailDto? dto = await _details.BuildAsync(id, ct);

        if (dto is null)
        {
            return NotFound();
        }

        return Ok(dto);
    }
}
