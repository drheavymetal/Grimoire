using System.ComponentModel.DataAnnotations;

namespace Grimoire.Server.Dtos;

/// <summary>A request to wrap a band as a gift (C22): the band, and an optional signed note.</summary>
public record CreateGiftRequest(
    [Required] Guid ArtistId,
    string? Note);

/// <summary>
/// A minted gift (C22): the opaque capability token to share, and the note echoed back. The token
/// is an encrypted payload (ASP.NET Data Protection) — it carries the band id without revealing it,
/// so no database row is needed and the recipient cannot read the band from the link.
/// </summary>
public record GiftDto(
    string Token,
    string? Note);

/// <summary>
/// What the recipient of a gift sees before they decide (C22): the signed note and the proxied
/// audio URL, but never the band — it stays face down until they choose to reveal it.
/// </summary>
public record GiftBlindDto(
    string? Note,
    string AudioUrl);
