using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Services;

/// <summary>
/// Builds the full <see cref="ArtistDetailDto"/> (identity, tags, discography, bloodline edges)
/// for one artist. Shared by <c>ArtistsController</c> (the artist page, feature B4/B5) and the
/// rite reveal (feature C4/C27) so both render the exact same shape from one place.
/// </summary>
public sealed class ArtistDetailBuilder
{
    private readonly GrimoireDbContext _db;

    public ArtistDetailBuilder(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns the detail for an artist, or null when the id is unknown.</summary>
    public async Task<ArtistDetailDto?> BuildAsync(Guid id, CancellationToken ct)
    {
        Artist? artist = await _db.Artists
            .AsNoTracking()
            .Include(a => a.Releases)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (artist is null)
        {
            return null;
        }

        List<ArtistEdgeDto> edges = await _db.ArtistEdges
            .AsNoTracking()
            .Where(e => e.FromId == id || e.ToId == id)
            .Select(e => new ArtistEdgeDto(e.FromId, e.ToId, e.Kind, e.BeginDate, e.EndDate, e.Instruments))
            .ToListAsync(ct);

        List<ReleaseDto> releases = artist.Releases
            .OrderBy(r => r.ReleaseDate ?? DateOnly.MaxValue)
            .ThenBy(r => r.Title)
            .Select(r => new ReleaseDto(r.Id, r.Mbid, r.Title, r.Type, r.ReleaseDate, r.CoverUrl))
            .ToList();

        return new ArtistDetailDto(
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
    }
}
