using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Movement V — catalogue curiosities derived straight from the discography (C24, C25). The
/// one-album band that said everything once and vanished; the hyperprolific project that puts out
/// more than it has lived. Both are pure reads over <c>releases</c> and <c>formed_year</c>, decided
/// by <see cref="CatalogueMath"/> so the boundary logic is the same one the tests bite.
/// </summary>
[ApiController]
[Route("api/catalogue")]
public class CatalogueController : ControllerBase
{
    private const int MaxRows = 60;

    private readonly GrimoireDbContext _db;

    public CatalogueController(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Bands with exactly one album and no other main release (C24). Live records and compilations
    /// are posthumous repackaging and do not disqualify the one-and-done story.
    /// </summary>
    [HttpGet("one-album")]
    public async Task<ActionResult<IReadOnlyList<OneAlbumBandDto>>> OneAlbum(CancellationToken ct = default)
    {
        // Per-band counts of the three "main" release types.
        var counts = await _db.Releases
            .AsNoTracking()
            .Where(r => r.Artist != null && r.Artist.Kind == ArtistKind.Group)
            .GroupBy(r => r.ArtistId)
            .Select(g => new
            {
                ArtistId = g.Key,
                Albums = g.Count(r => r.Type == ReleaseType.Album),
                Eps = g.Count(r => r.Type == ReleaseType.Ep),
                Demos = g.Count(r => r.Type == ReleaseType.Demo),
            })
            .ToListAsync(ct);

        List<Guid> oneAlbumIds = counts
            .Where(c => CatalogueMath.IsOneAlbumBand(c.Albums, c.Eps, c.Demos))
            .Select(c => c.ArtistId)
            .ToList();

        if (oneAlbumIds.Count == 0)
        {
            return Ok(Array.Empty<OneAlbumBandDto>());
        }

        // The single album of each, plus the band's identity.
        List<OneAlbumBandDto> result = await _db.Releases
            .AsNoTracking()
            .Where(r => r.Type == ReleaseType.Album && oneAlbumIds.Contains(r.ArtistId) && r.Artist != null)
            .OrderBy(r => r.Artist!.Name)
            .Select(r => new OneAlbumBandDto(
                r.ArtistId,
                r.Artist!.Name,
                r.Artist!.Rank,
                r.Artist!.Country,
                r.Id,
                r.Mbid,
                r.Title,
                r.ReleaseDate))
            .Take(MaxRows)
            .ToListAsync(ct);

        return Ok(result);
    }

    /// <summary>
    /// Bands that released more than they have lived — releases per year of existence above 1 (C25).
    /// Ordered by that ratio, most relentless first. Needs a formation year, so bands without one
    /// are not eligible (never invented).
    /// </summary>
    [HttpGet("hyperprolific")]
    public async Task<ActionResult<IReadOnlyList<ProlificBandDto>>> Hyperprolific(CancellationToken ct = default)
    {
        int currentYear = DateTime.UtcNow.Year;

        var perBand = await _db.Releases
            .AsNoTracking()
            .Where(r => r.Artist != null && r.Artist.Kind == ArtistKind.Group && r.Artist.FormedYear != null)
            .GroupBy(r => new { r.ArtistId, r.Artist!.Name, r.Artist.Rank, FormedYear = r.Artist.FormedYear!.Value })
            .Select(g => new
            {
                g.Key.ArtistId,
                g.Key.Name,
                g.Key.Rank,
                g.Key.FormedYear,
                ReleaseCount = g.Count(),
            })
            .ToListAsync(ct);

        List<ProlificBandDto> result = perBand
            .Where(b => CatalogueMath.IsHyperprolific(b.ReleaseCount, b.FormedYear, currentYear))
            .Select(b => new ProlificBandDto(
                b.ArtistId,
                b.Name,
                b.Rank,
                b.FormedYear,
                b.ReleaseCount,
                CatalogueMath.ProlificacyRatio(b.ReleaseCount, b.FormedYear, currentYear)))
            .OrderByDescending(b => b.Ratio)
            .ThenBy(b => b.Name)
            .Take(MaxRows)
            .ToList();

        return Ok(result);
    }
}
