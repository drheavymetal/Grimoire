using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>
/// One star on the Atlas (C18/B22): an artist at a 2D position projected from its embedding. Rank
/// tints it (null rank is a plain star); the id opens the artist page on click.
/// </summary>
public record AtlasStarDto(
    Guid Id,
    string Name,
    ArtistKind Kind,
    Rank? Rank,
    double X,
    double Y);

/// <summary>The caller's taste, projected onto the same plane as the stars ("you are here").</summary>
public record AtlasPointDto(double X, double Y);

/// <summary>
/// The Atlas payload: every projected star, plus the caller's taste position when it is known.
/// <see cref="Taste"/> is null for an anonymous caller or one without a taste vector — a designed
/// empty state, never a fabricated point.
/// </summary>
public record AtlasDto(
    IReadOnlyList<AtlasStarDto> Stars,
    AtlasPointDto? Taste);
