using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>
/// One tile on the wall of covers (C6): the release-group MBID that feeds the cover proxy (B6), and
/// the band it belongs to so the tile links through. Missing art is a designed empty state, not a
/// broken tile (R2).
/// </summary>
public record CoverWallItemDto(
    Guid ReleaseId,
    Guid Mbid,
    string Title,
    DateOnly? ReleaseDate,
    Guid ArtistId,
    string ArtistName,
    Rank? ArtistRank);
