using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>
/// A one-album band (C24): a band with exactly one album and nothing else. The single record is
/// carried so the front can show it and open its cover (B6).
/// </summary>
public record OneAlbumBandDto(
    Guid Id,
    string Name,
    Rank? Rank,
    string? Country,
    Guid AlbumId,
    Guid AlbumMbid,
    string AlbumTitle,
    DateOnly? AlbumDate);

/// <summary>
/// A hyperprolific band (C25): more releases than it has been alive years. <see cref="Ratio"/> is
/// releases per year of existence — the higher, the more relentless.
/// </summary>
public record ProlificBandDto(
    Guid Id,
    string Name,
    Rank? Rank,
    int FormedYear,
    int ReleaseCount,
    double Ratio);
