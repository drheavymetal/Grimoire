using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>A band a remembered musician played in (feature C12), enough to name it and click through.</summary>
public record MemoriamBandDto(Guid Id, string Name, Rank? Rank);

/// <summary>
/// One entry in In Memoriam (feature C12): a musician who has died, with their date and place of
/// death (Wikidata P570/P20) and the bands they played in. The tone is deliberately plain — a
/// record of who they were, not a spectacle. Only people Wikidata asserts have died appear here;
/// a null death place is left blank, never guessed.
/// </summary>
public record MemoriamEntryDto(
    Guid Id,
    string Name,
    DateOnly DeathDate,
    string? DeathPlace,
    IReadOnlyList<MemoriamBandDto> Bands);
