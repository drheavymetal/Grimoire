using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>
/// A node in a lineage graph (movement IV). Carries just enough to paint and label it and to click
/// through to the artist page: identity, whether it is a person or a band, its rank (may be null),
/// and a <see cref="Role"/> that marks the special nodes of a view ("ego"/"source"/"target"),
/// everything else "node".
/// </summary>
public record GraphNodeDto(
    Guid Id,
    string Name,
    ArtistKind Kind,
    Rank? Rank,
    string Role);

/// <summary>
/// An edge in a lineage graph. <see cref="Kind"/> is "member" (a MemberOf relation, person↔band) or
/// "influence" (an InfluencedBy relation, band→band). <see cref="Label"/> is an optional instrument
/// list for member edges.
/// </summary>
public record GraphEdgeDto(
    Guid Source,
    Guid Target,
    string Kind,
    string? Label);

/// <summary>A lineage graph: nodes plus the edges between them (B16 Bloodline, C17 grimoire graph).</summary>
public record GraphDto(
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphEdgeDto> Edges);

/// <summary>
/// A shortest path between two bands (B19 Six Degrees). <see cref="Nodes"/> is the ordered chain
/// (band, member, band, …); <see cref="Degrees"/> counts the band-to-band hops. Empty
/// <see cref="Nodes"/> means the two are not connected.
/// </summary>
public record PathDto(
    IReadOnlyList<GraphNodeDto> Nodes,
    int Degrees);

/// <summary>One destination in a diaspora (B11): the band a departing member joined next.</summary>
public record DiasporaDestinationDto(
    Guid BandId,
    string BandName,
    Rank? BandRank,
    DateOnly? JoinedYear);

/// <summary>A member who left a broken-up band, and the bands they went to afterward (B11).</summary>
public record DiasporaMemberDto(
    Guid MemberId,
    string MemberName,
    DateOnly? LeftDate,
    IReadOnlyList<DiasporaDestinationDto> Destinations);

/// <summary>The diaspora of one band: its departed members and where each went (B11).</summary>
public record DiasporaDto(
    GraphNodeDto Band,
    IReadOnlyList<DiasporaMemberDto> Members);

/// <summary>One band a musician played in (B3, search by member): the band plus their stint in it.</summary>
public record MemberBandDto(
    Guid BandId,
    string BandName,
    ArtistKind BandKind,
    Rank? BandRank,
    DateOnly? BeginDate,
    DateOnly? EndDate,
    string[] Instruments);

/// <summary>All the bands one musician played in (B3).</summary>
public record MemberBandsDto(
    GraphNodeDto Member,
    IReadOnlyList<MemberBandDto> Bands);

/// <summary>A neighbour found between two bands' interpolated midpoint (C5, the missing link).</summary>
public record MissingLinkNeighbourDto(
    Guid Id,
    string Name,
    ArtistKind Kind,
    Rank? Rank,
    double Distance);

/// <summary>The bands that live between two others in embedding space (C5).</summary>
public record MissingLinkDto(
    GraphNodeDto From,
    GraphNodeDto To,
    IReadOnlyList<MissingLinkNeighbourDto> Between);

/// <summary>A guided walk through the lineage graph (C8, Rabbit Hole): an ordered chain of artists.</summary>
public record RabbitHoleDto(
    IReadOnlyList<GraphNodeDto> Steps);
