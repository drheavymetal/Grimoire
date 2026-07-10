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
    string Title,
    ReleaseType Type,
    DateOnly? ReleaseDate,
    string? CoverUrl);

/// <summary>A bloodline edge, for the artist page (feature B16, populated later).</summary>
public record ArtistEdgeDto(
    Guid FromId,
    Guid ToId,
    EdgeKind Kind,
    DateOnly? BeginDate,
    DateOnly? EndDate,
    string[] Instruments);

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
    string? ImageUrl,
    Dictionary<string, string>? Links,
    IReadOnlyList<ReleaseDto> Releases,
    IReadOnlyList<ArtistEdgeDto> Edges);
