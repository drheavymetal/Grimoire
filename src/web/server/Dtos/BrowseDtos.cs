using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>
/// A compact band card for the browse door (the "see all" list behind a clicked tag or theme).
/// This is explicit browse, NOT the blind rite, so the band is named — id, name, rarity rank,
/// country and kind, everything a card needs and nothing the reveal owns.
/// </summary>
public record BandCardDto(
    Guid Id,
    string Name,
    Rank? Rank,
    string? Country,
    ArtistKind Kind);

/// <summary>
/// A page of the browse door: the total number of bands matching the filter (for paging) and the
/// current page of cards. An empty match yields <see cref="Total"/> 0 and no bands — never invented.
/// </summary>
public record BrowseResultDto(
    int Total,
    IReadOnlyList<BandCardDto> Bands)
{
    /// <summary>The empty page, for a blank needle or an unknown mined theme.</summary>
    public static BrowseResultDto Empty { get; } = new(0, []);
}
