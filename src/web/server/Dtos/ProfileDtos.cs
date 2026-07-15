using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

/// <summary>
/// The user profile page: a portrait of a grimoire drawn from the bands the user has summoned, plus
/// the size of the editable anchor set (the HYBRID taste model). Every list is honest — an empty
/// grimoire yields zeroes, a null <see cref="DeepestCut"/> and empty breakdowns, never invented data.
/// </summary>
public record ProfileDto(
    int DepthScore,
    int SummonedCount,
    int AnchorCount,
    BandCardDto? DeepestCut,
    IReadOnlyList<RankCountDto> RankBreakdown,
    IReadOnlyList<DecadeCountDto> ByDecade,
    IReadOnlyList<CountryCountDto> ByCountry,
    IReadOnlyList<GenreCountDto> ByGenre,
    // The caller's public friend handle (FRIENDS wave), or null until they claim one.
    string? Handle);

/// <summary>Sets the caller's public friend handle (FRIENDS wave): 3-30 chars of [a-z0-9_].</summary>
public record SetHandleRequest(
    string Handle);

/// <summary>How many summoned bands sit in a given rarity tier. A null <see cref="Rank"/> is the
/// unranked bucket (listeners unknown) — kept, never dropped, never invented into a tier.</summary>
public record RankCountDto(
    Rank? Rank,
    int Count);

/// <summary>How many summoned bands formed in a given decade (the year floored to its ten).</summary>
public record DecadeCountDto(
    int Decade,
    int Count);

/// <summary>How many summoned bands hail from a given country.</summary>
public record CountryCountDto(
    string Country,
    int Count);

/// <summary>How many summoned bands carry a given genre tag.</summary>
public record GenreCountDto(
    string Tag,
    int Count);

/// <summary>The body of "add an anchor": the band to pin.</summary>
public record AddAnchorRequest(
    Guid ArtistId);

/// <summary>
/// The body of "re-seed my taste" (the profile re-pick, like the sign-up cold start). <see cref="ArtistIds"/>
/// are the bands the user just picked; <see cref="Mode"/> is <c>"fresh"</c> (replace the anchor set and start
/// over) or <c>"add"</c> (union the picks into the existing anchors), case-insensitive — anything else is a 400.
/// </summary>
public record ReseedRequest(
    IReadOnlyList<Guid> ArtistIds,
    string Mode);

/// <summary>
/// The outcome of rebuilding the taste vector from the anchor set. <see cref="AnchorsUsed"/> counts
/// only anchors that HAD an embedding (null-embedding anchors are skipped); <see cref="TasteSet"/> is
/// false when there were no usable anchors and nothing was written. <see cref="DepthScore"/> is the
/// current value (it is over summons, not anchors, so a rebuild leaves it untouched).
/// </summary>
public record RebuildResultDto(
    int AnchorsUsed,
    bool TasteSet,
    int DepthScore);
