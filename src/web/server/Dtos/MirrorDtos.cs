using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

// ---------------------------------------------------------------------------
// The mirror (feature C20)
// ---------------------------------------------------------------------------

/// <summary>
/// The mirror (C20): "X% of the bands you rejected blind belong to your favourite genre." Computed
/// from the rite history alone. <see cref="HasData"/> is false when there is nothing to reflect yet
/// (no favourite genre, or nothing banished) — a designed empty state, never a fabricated number.
/// </summary>
public record MirrorDto(
    bool HasData,
    string? FavouriteTag,
    int BanishedTotal,
    int BanishedMatching,
    double Fraction);

// ---------------------------------------------------------------------------
// Your trajectory (feature C16)
// ---------------------------------------------------------------------------

/// <summary>
/// The path a user's taste travelled (C16): one point per snapshot in chronological order, plus the
/// total drift from first to last. Each point carries its Atlas-plane projection when it could be
/// placed, so the front can draw the walk across the same map as the stars.
/// </summary>
public record TrajectoryDto(
    IReadOnlyList<TrajectoryPointDto> Points,
    double TotalDrift);

/// <summary>
/// One snapshot on the trajectory: when it was taken, the depth score then, the drift from the
/// previous snapshot (0 for the first), and its Atlas-plane position (null when unprojectable).
/// </summary>
public record TrajectoryPointDto(
    DateTimeOffset CreatedAt,
    int DepthScore,
    double Drift,
    double? X,
    double? Y);

// ---------------------------------------------------------------------------
// Anti-recommendation (feature B25)
// ---------------------------------------------------------------------------

/// <summary>
/// The anti-recommendation (B25): "this band will repel you, and here is why." It is the band
/// nearest the user's repulsion vector. <see cref="HasData"/> is false until the user has banished
/// something (no repulsion → nothing to repel from) — an honest empty state.
/// </summary>
public record AntiRecDto(
    bool HasData,
    AntiRecBandDto? Band);

/// <summary>
/// The band the engine predicts you will reject, revealed (this is a warning, not a blind rite),
/// with why: how close it sits to what you have banished, how far from what you love, and which of
/// its tags overlap the genres you rejected.
/// </summary>
public record AntiRecBandDto(
    Guid Id,
    string Name,
    string? Country,
    int? FormedYear,
    Rank? Rank,
    IReadOnlyList<string> Tags,
    double DistanceToRepulsion,
    double DistanceToTaste,
    IReadOnlyList<string> SharedRejectedTags);

// ---------------------------------------------------------------------------
// Dark Twin (feature B18)
// ---------------------------------------------------------------------------

/// <summary>
/// The Dark Twin (B18): the user whose taste is closest to yours yet whose collection is most
/// disjoint. Anonymous — no identity, only the numbers and the discoveries they can offer.
/// <see cref="HasData"/> is false with too few users (the honest empty state).
/// <see cref="TheirsOnly"/> is what they have summoned that you have not.
/// </summary>
public record DarkTwinDto(
    bool HasData,
    double TasteSimilarity,
    double Disjointness,
    int SharedCount,
    IReadOnlyList<ArtistSummaryDto> TheirsOnly);

// ---------------------------------------------------------------------------
// Gaps (feature B23)
// ---------------------------------------------------------------------------

/// <summary>
/// The gaps (B23): the decades, countries and subgenres of the catalogue the user has never
/// summoned — the dark regions of the Atlas, cast as lists. Each bucket carries how many bands
/// live there, so the biggest untouched regions surface first.
/// </summary>
public record GapsDto(
    IReadOnlyList<GapBucketDto> Decades,
    IReadOnlyList<GapBucketDto> Countries,
    IReadOnlyList<GapBucketDto> Subgenres);

/// <summary>One untouched region: its label (a decade, country code or tag) and its catalogue size.</summary>
public record GapBucketDto(string Label, int CatalogueCount);
