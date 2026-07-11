using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>A label in the index (B21): identity plus how many releases we hold on it.</summary>
public record LabelSummaryDto(
    Guid Id,
    string Name,
    string? Country,
    int ReleaseCount);

/// <summary>
/// One release on a label's page (B21), carrying the band it belongs to so the label works as a
/// door: click the release's band to open its page. <see cref="Mbid"/> feeds the cover proxy (B6).
/// </summary>
public record LabelReleaseDto(
    Guid Id,
    Guid Mbid,
    string Title,
    ReleaseType Type,
    DateOnly? ReleaseDate,
    Guid ArtistId,
    string ArtistName,
    Rank? ArtistRank);

/// <summary>A label's page (B21): the label and every release we hold on it, newest first.</summary>
public record LabelDetailDto(
    Guid Id,
    string Name,
    string? Country,
    IReadOnlyList<LabelReleaseDto> Releases);
