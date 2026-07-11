using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>
/// A musician who plays a rare instrument (feature C15), and the band the credit is on, so the
/// list can click through to either.
/// </summary>
public record RareInstrumentPlayerDto(
    Guid ArtistId,
    string Name,
    Guid BandId,
    string BandName,
    Rank? BandRank);

/// <summary>
/// A rare instrument and who plays it (feature C15): violin, bagpipe, hurdy gurdy and the rest of
/// the folk/orchestral colour outside the standard rock kit. <see cref="PlayerCount"/> is the number
/// of distinct musicians credited with it. Read straight off the real <c>credits.instrument</c>
/// column — nothing invented, and an instrument nobody plays simply does not appear.
/// </summary>
public record RareInstrumentDto(
    string Instrument,
    int PlayerCount,
    IReadOnlyList<RareInstrumentPlayerDto> Players);
