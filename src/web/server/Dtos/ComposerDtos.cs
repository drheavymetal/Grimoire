namespace Grimoire.Server.Dtos;

/// <summary>
/// One musical work (a composition) on a composer's page (movement VII, D11). A work is not a
/// release: a symphony has one composer and many performances, which the band/member model does
/// not capture. <see cref="Kind"/> is MusicBrainz's work type (symphony, opera, song…) and is
/// null when MusicBrainz gives none — never invented.
/// </summary>
public record WorkDto(
    Guid Id,
    Guid Mbid,
    string Title,
    string? Kind);

/// <summary>
/// A group of works sharing one <see cref="Kind"/>, the way a composer's discography is grouped.
/// <see cref="Kind"/> is null for the "unclassified" group (works MusicBrainz left untyped): the
/// front shows them under their own heading, never hidden (D11 / classical-data §7).
/// </summary>
public record WorkGroupDto(
    string? Kind,
    IReadOnlyList<WorkDto> Works);

/// <summary>A composer linked from another in the master–apprentice or influence lineage.</summary>
public record ComposerLinkDto(
    Guid Id,
    string Name);

/// <summary>
/// A composer's lineage (D11): the pedagogical chain (teacher/student, e.g. Fauré→Boulanger→Glass)
/// and declared influence (Wikidata P737). The textual lists carry the immediate relations for a
/// plain, clickable reading; the <see cref="Graph"/> is the same relations as an ego graph for the
/// shared GraphCanvas (D18). All can be empty — sparse lineage is real (only 12 teacher/student
/// edges in the whole corpus), so an empty lineage renders a designed empty state, never a stub.
/// </summary>
public record ComposerLineageDto(
    IReadOnlyList<ComposerLinkDto> Teachers,
    IReadOnlyList<ComposerLinkDto> Students,
    IReadOnlyList<ComposerLinkDto> Influences,
    GraphDto Graph);

/// <summary>
/// The composer page payload (movement VII). Unlike the band ficha it carries NO Gantt, NO members
/// and NO rank (D11 — classical listeners lie): the hero is the grouped list of works, plus the two
/// lineages. Identity (name, country, tags, bio) comes from the shared artist detail the page
/// already holds; this adds only what the composer view needs beyond it.
/// </summary>
public record ComposerDetailDto(
    int WorkCount,
    IReadOnlyList<WorkGroupDto> WorkGroups,
    ComposerLineageDto Lineage);
