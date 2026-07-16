using Grimoire.Library.Models;
using Grimoire.Library.Services;

namespace Grimoire.Server.Dtos;

/// <summary>Compact artist shape for search results (feature B1).</summary>
public record ArtistSummaryDto(
    Guid Id,
    string Name,
    string? Country,
    int? FormedYear,
    Rank? Rank);

/// <summary>A release, for the discography list on the artist page (feature B5).</summary>
public record ReleaseDto(
    Guid Id,
    Guid Mbid,
    string Title,
    ReleaseType Type,
    DateOnly? ReleaseDate,
    string? CoverUrl);

/// <summary>
/// A bloodline edge, for the lineup timeline (B7/B8), the member page (B10) and the
/// bloodline graph (B16). <see cref="CounterpartId"/> is the artist on the other end
/// from the artist being viewed — the member when viewing a band, the band when
/// viewing a person — so the timeline can label each row without a second lookup.
/// </summary>
public record ArtistEdgeDto(
    Guid FromId,
    Guid ToId,
    EdgeKind Kind,
    DateOnly? BeginDate,
    DateOnly? EndDate,
    string[] Instruments,
    Guid CounterpartId,
    string CounterpartName,
    ArtistKind CounterpartKind);

/// <summary>Full artist shape for the artist page (feature B4).</summary>
public record ArtistDetailDto(
    Guid Id,
    string Name,
    string? SortName,
    ArtistKind Kind,
    string? Country,
    string? City,
    int? FormedYear,
    int? DissolvedYear,
    int? Listeners,
    Rank? Rank,
    string[] Tags,
    // Every Wikipedia biography this band actually has text for, English first then by language code
    // (Services.ArtistBiographies.Merge). A list rather than an abstract/abstractUrl pair because the
    // reader's language is the client's to pick: a Spanish reader gets eswiki when it exists, English
    // when it does not — labelled, never translated (Invariant 1: no paid service; Invariant 5: no
    // invented text). Empty is the common case and an honest gap.
    IReadOnlyList<ArtistBiographyView> Biographies,
    string? ImageUrl,
    Dictionary<string, string>? Links,
    IReadOnlyList<ReleaseDto> Releases,
    IReadOnlyList<ArtistEdgeDto> Edges,
    // Lyrical themes from Metal Archives (D48/Q4). Empty until the MA pass matches the band —
    // an empty list is a real gap, never invented (Invariant 5).
    IReadOnlyList<string> LyricalThemes,
    // Metal Archives' own genre string (e.g. "Black Metal (early); Ambient (later)"). Null until matched.
    string? MetalArchivesGenre);
