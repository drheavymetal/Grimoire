using Grimoire.Library.Data;
using Grimoire.Library.Models;
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

    /// <summary>
    /// Per-release credits for a band's discography (feature B9): who performed on each release
    /// (official member vs guest, with their instruments) and who produced, engineered, mixed or
    /// mastered it. Keyed by release id so the front matches it to the discography it already holds.
    /// Reads the real <c>credits</c> rows; releases the ETL never reached simply do not appear, and
    /// the front renders a designed "no credits" state for them (R2 — the ficha degrades with dignity).
    /// </summary>
    [HttpGet("{id:guid}/credits")]
    public async Task<ActionResult<IReadOnlyList<ReleaseCreditsDto>>> Credits(Guid id, CancellationToken ct = default)
    {
        bool exists = await _db.Artists.AsNoTracking().AnyAsync(a => a.Id == id, ct);
        if (!exists)
        {
            return NotFound();
        }

        // Credits sit on releases; a release belongs to this band, and each credit's artist is the
        // performer (a member or a guest). Join to the performer for their name and rank.
        var rows = await _db.Credits
            .AsNoTracking()
            .Where(c => c.ReleaseId != null
                && _db.Releases.Any(r => r.Id == c.ReleaseId && r.ArtistId == id))
            .Join(
                _db.Artists.AsNoTracking(),
                c => c.ArtistId,
                a => a.Id,
                (c, a) => new { c.ReleaseId, c.ArtistId, a.Name, a.Rank, c.Role, c.Instrument, c.IsGuest })
            .ToListAsync(ct);

        IReadOnlyList<ReleaseCreditsDto> grouped = CreditGrouping.Group(
            rows.Select(r => new CreditGrouping.CreditRow(
                r.ReleaseId!.Value, r.ArtistId, r.Name, r.Rank, r.Role, r.Instrument, r.IsGuest)));

        return Ok(grouped);
    }

    /// <summary>
    /// "The disc where everything changed" (feature B12): the release with the greatest lineup
    /// turnover around its date, and who joined and left near it. Reuses the interval logic of the
    /// Gantt (<see cref="LineupTurnover"/> over <c>LineupIntervalResolver</c>). Returns 204 No Content
    /// when no dated release sees any change — an honest empty state, never invented drama.
    /// </summary>
    [HttpGet("{id:guid}/pivotal-release")]
    public async Task<ActionResult<PivotalReleaseDto>> PivotalRelease(Guid id, CancellationToken ct = default)
    {
        bool exists = await _db.Artists.AsNoTracking().AnyAsync(a => a.Id == id, ct);
        if (!exists)
        {
            return NotFound();
        }

        List<ArtistEdge> edges = await _db.ArtistEdges
            .AsNoTracking()
            .Where(e => (e.FromId == id || e.ToId == id) && e.Kind == EdgeKind.MemberOf)
            .ToListAsync(ct);

        var datedReleases = await _db.Releases
            .AsNoTracking()
            .Where(r => r.ArtistId == id && r.ReleaseDate != null)
            .Select(r => new { r.Id, r.Title, Date = r.ReleaseDate!.Value })
            .ToListAsync(ct);

        LineupTurnover.ReleaseTurnover? pivotal = LineupTurnover.MostPivotal(
            id,
            datedReleases.Select(r => (r.Id, r.Date)).ToList(),
            edges);

        if (pivotal is null)
        {
            return NoContent();
        }

        var byId = datedReleases.ToDictionary(r => r.Id, r => r);
        var release = byId[pivotal.ReleaseId];

        // Resolve the member names for the joined/left sets in one query.
        List<Guid> memberIds = pivotal.Joined.Concat(pivotal.Left).Distinct().ToList();
        Dictionary<Guid, string> names = await _db.Artists
            .AsNoTracking()
            .Where(a => memberIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        List<TurnoverMemberDto> ToMembers(IReadOnlyList<Guid> ids)
        {
            return ids
                .Select(m => new TurnoverMemberDto(m, names.TryGetValue(m, out string? n) ? n : string.Empty))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var dto = new PivotalReleaseDto(
            release.Id,
            release.Title,
            release.Date.Year,
            pivotal.Score,
            ToMembers(pivotal.Joined),
            ToMembers(pivotal.Left));

        return Ok(dto);
    }
}
