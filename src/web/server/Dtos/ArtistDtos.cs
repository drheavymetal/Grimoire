using Grimoire.Library.Models;

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
    string? Abstract,
    string? AbstractUrl,
    string? ImageUrl,
    Dictionary<string, string>? Links,
    IReadOnlyList<ReleaseDto> Releases,
    IReadOnlyList<ArtistEdgeDto> Edges);
