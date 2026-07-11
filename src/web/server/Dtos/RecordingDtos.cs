using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>
/// One track of a release (B5): its 1-based position, title, and length in milliseconds.
/// <see cref="LengthMs"/> is null when MusicBrainz never timed it — the front renders an em dash,
/// never a fabricated duration (C7 degrades honestly).
/// </summary>
public record TrackDto(
    int Position,
    string Title,
    int? LengthMs);

/// <summary>
/// A band placed on the duration axis (C7, funeral doom ↔ grindcore): its mean track length in
/// milliseconds, computed over its timed recordings only (null lengths excluded, never counted as
/// zero), plus how many tracks that average rests on so the client can show the sample size.
/// </summary>
public record ArtistDurationDto(
    Guid Id,
    string Name,
    Rank? Rank,
    string? Country,
    int TimedTrackCount,
    double AverageMs);

/// <summary>One lyrical theme approximated from a band's titles and how many titles evoke it (C21).</summary>
public record ThemeCountDto(
    string Theme,
    int Count);

/// <summary>
/// The title-mining result for a band (C21): the themes its titles evoke, most present first, and
/// the number of titles the approximation ran over. It is an <b>approximation</b> from titles, not a
/// curated lyrical fact — the UI states so.
/// </summary>
public record ArtistThemesDto(
    int TitleCount,
    IReadOnlyList<ThemeCountDto> Themes);

/// <summary>
/// One cross-artist cover in the version graph (C10): who was covered, who covered them, the
/// MusicBrainz relation (remix, edit, instrumental…), and the covered song's title. The graph nodes
/// and edges are carried alongside in <see cref="VersionGraphDto"/>; this list gives the song each
/// edge stands for, which the graph itself cannot show.
/// </summary>
public record CoverEdgeDto(
    Guid OriginalArtistId,
    string OriginalArtistName,
    Guid CoverArtistId,
    string CoverArtistName,
    string Relation,
    string Title);

/// <summary>
/// The version graph for one band (C10): the shared graph payload (artists as nodes, cover
/// relations as edges labelled with the relation) plus the ordered list of the individual covers
/// with their song titles. Empty when the band's recordings were never covered by anyone else — a
/// designed empty state, not an error (most of the underground has no cross-artist cover).
/// </summary>
public record VersionGraphDto(
    GraphDto Graph,
    IReadOnlyList<CoverEdgeDto> Versions);
