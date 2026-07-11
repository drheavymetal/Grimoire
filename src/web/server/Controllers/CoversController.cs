using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Cover art proxy (feature B6) and the wall of covers (C6). Covers resolve from the Cover Art
/// Archive through an on-disk cache; the browser only ever talks to this endpoint, never to CAA.
/// </summary>
[ApiController]
[Route("api/covers")]
public class CoversController : ControllerBase
{
    private const int MaxWall = 120;

    private readonly CoverArtCache _covers;
    private readonly GrimoireDbContext _db;

    public CoversController(CoverArtCache covers, GrimoireDbContext db)
    {
        _covers = covers;
        _db = db;
    }

    /// <summary>
    /// The wall of covers (C6): a grid of real album covers. Ordered by listeners so the archive is
    /// most likely to hold the art (coverage is worst in the deep underground — R2), each cover
    /// links to its band. The dominant-palette variant (C6/C26) is a declared gap: reading pixels
    /// from the cross-origin proxied image would taint the canvas, so it is not computed here.
    /// </summary>
    [HttpGet("wall")]
    public async Task<ActionResult<IReadOnlyList<CoverWallItemDto>>> Wall(
        [FromQuery] int limit = 60,
        CancellationToken ct = default)
    {
        int take = Math.Clamp(limit, 1, MaxWall);

        // Take the top DISTINCT bands by listeners (the archive is likeliest to hold their art), so a
        // prolific band contributes one tile, not its whole discography — a diverse grid of bands.
        List<Guid> bandIds = await _db.Artists
            .AsNoTracking()
            .Where(a => a.Kind == ArtistKind.Group
                && a.Listeners != null
                && a.Releases.Any(r => r.Type == ReleaseType.Album))
            .OrderByDescending(a => a.Listeners)
            .Take(take)
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (bandIds.Count == 0)
        {
            return Ok(Array.Empty<CoverWallItemDto>());
        }

        // One album per band (the earliest — usually the debut, the recognisable sleeve).
        var rows = await _db.Releases
            .AsNoTracking()
            .Where(r => r.Type == ReleaseType.Album && bandIds.Contains(r.ArtistId) && r.Artist != null)
            .Select(r => new
            {
                Dto = new CoverWallItemDto(r.Id, r.Mbid, r.Title, r.ReleaseDate, r.ArtistId, r.Artist!.Name, r.Artist!.Rank),
                Listeners = r.Artist!.Listeners,
            })
            .ToListAsync(ct);

        List<CoverWallItemDto> albums = rows
            .GroupBy(x => x.Dto.ArtistId)
            .Select(g => g.OrderBy(x => x.Dto.ReleaseDate ?? DateOnly.MaxValue).ThenBy(x => x.Dto.Title).First())
            .OrderByDescending(x => x.Listeners)
            .Select(x => x.Dto)
            .ToList();

        return Ok(albums);
    }

    /// <summary>Front cover for a release-group MBID. 404 when the archive has none.</summary>
    [HttpGet("release-group/{mbid:guid}")]
    public async Task<IActionResult> GetReleaseGroupFront(Guid mbid, CancellationToken ct = default)
    {
        CoverResult result = await _covers.GetAsync(mbid, ct);

        switch (result.Outcome)
        {
            case CoverOutcome.Found:
                // The cache is durable; let the browser hold the image for a week.
                Response.Headers.CacheControl = "public, max-age=604800";
                return PhysicalFile(result.FilePath!, "image/jpeg");

            case CoverOutcome.NotFound:
                // The miss is cached too, but let the browser back off for a day.
                Response.Headers.CacheControl = "public, max-age=86400";
                return NotFound();

            default:
                // Transient upstream failure — tell the client it is worth retrying later.
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
