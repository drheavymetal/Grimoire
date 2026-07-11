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

    // A band needs at least this many timed tracks before its mean track length is trustworthy on
    // the duration axis (C7) — enough that one outlier piece cannot define the whole catalogue.
    private const int MinTimedTracks = 20;

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

    /// <summary>
    /// The duration axis (C7): bands ranked by their mean track length, the pole no genre tag
    /// captures — funeral doom at one end, grindcore at the other. <paramref name="pole"/> picks the
    /// end: <c>long</c> (default) for the longest averages, <c>short</c> for the shortest. The
    /// average is over a band's <b>timed</b> recordings only (MusicBrainz nulls are absences, not
    /// zeros); a band needs at least <see cref="MinTimedTracks"/> timed tracks to appear, so a single
    /// six-hour ambient piece cannot masquerade as a catalogue. It is an axis of curiosity, not a
    /// claim of genre.
    /// </summary>
    [HttpGet("duration-axis")]
    public async Task<ActionResult<IReadOnlyList<ArtistDurationDto>>> DurationAxis(
        [FromQuery] string pole = "long",
        [FromQuery] int limit = 30,
        CancellationToken ct = default)
    {
        int take = Math.Clamp(limit, 1, MaxRows);
        bool shortest = string.Equals(pole, "short", StringComparison.OrdinalIgnoreCase);

        // Set-based: average the non-null lengths per band, keep only bands with enough timed
        // tracks, then order by that average toward the requested pole. avg() already ignores NULLs.
        var query = _db.Recordings
            .AsNoTracking()
            .Where(rec => rec.LengthMs != null)
            .GroupBy(rec => _db.Releases.Where(r => r.Id == rec.ReleaseId).Select(r => r.ArtistId).FirstOrDefault())
            .Select(g => new
            {
                ArtistId = g.Key,
                TimedTrackCount = g.Count(),
                AverageMs = g.Average(rec => (double)rec.LengthMs!.Value),
            })
            .Where(g => g.TimedTrackCount >= MinTimedTracks);

        var ranked = shortest
            ? query.OrderBy(g => g.AverageMs).ThenBy(g => g.ArtistId)
            : query.OrderByDescending(g => g.AverageMs).ThenBy(g => g.ArtistId);

        var top = await ranked.Take(take).ToListAsync(ct);

        if (top.Count == 0)
        {
            return Ok(Array.Empty<ArtistDurationDto>());
        }

        // Keep only bands (a person's "average track" is not a discovery axis) and attach identity.
        List<Guid> artistIds = top.Select(t => t.ArtistId).ToList();
        Dictionary<Guid, (string Name, Rank? Rank, string? Country, ArtistKind Kind)> meta = await _db.Artists
            .AsNoTracking()
            .Where(a => artistIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name, a.Rank, a.Country, a.Kind })
            .ToDictionaryAsync(a => a.Id, a => (a.Name, a.Rank, a.Country, a.Kind), ct);

        List<ArtistDurationDto> result = top
            .Where(t => meta.TryGetValue(t.ArtistId, out (string Name, Rank? Rank, string? Country, ArtistKind Kind) m) && m.Kind == ArtistKind.Group)
            .Select(t =>
            {
                (string Name, Rank? Rank, string? Country, ArtistKind Kind) m = meta[t.ArtistId];
                return new ArtistDurationDto(t.ArtistId, m.Name, m.Rank, m.Country, t.TimedTrackCount, t.AverageMs);
            })
            .ToList();

        return Ok(result);
    }
}
