using System.ComponentModel.DataAnnotations;
using Grimoire.Library.Models;

namespace Grimoire.Server.Dtos;

// ---------------------------------------------------------------------------
// Cold start (DECISIONS D15)
// ---------------------------------------------------------------------------

/// <summary>
/// A pickable band for the cold-start "choose five bands" screen. This screen is NOT blind:
/// the user is choosing bands they already like to seed their taste, so name and origin show.
/// </summary>
public record SeedCandidateDto(
    Guid Id,
    string Name,
    string? Country,
    int? FormedYear);

/// <summary>Cold-start seed request: the artists the user picked (five, by design — DECISIONS D15).</summary>
public record SeedRequest(
    [Required][MinLength(1)] IReadOnlyList<Guid> ArtistIds);

/// <summary>Cold-start via Last.fm scrobbles (feature C1). Gated on a Last.fm API key.</summary>
public record LastFmImportRequest(
    [Required] string Username);

/// <summary>Whether the caller already has a taste vector, so the front knows to run cold start.</summary>
public record TasteStatusDto(
    bool HasTaste,
    int SummonedCount,
    DateTimeOffset? UpdatedAt,
    int DepthScore);

// ---------------------------------------------------------------------------
// Serving The Rite (feature B13, B14)
// ---------------------------------------------------------------------------

/// <summary>
/// A request to serve one blind tasting. <see cref="Comfort"/> is the Comfort ↔ Abyss slider
/// in [0, 1] (0 = nearest, 1 = deepest). The optional hard filters are decade and country only
/// (feature C13): format is not modelled and rank is null, so neither is offered — choosing by
/// rank would render a lie.
/// </summary>
public record ServeRequest(
    double Comfort = 0.5,
    string? Country = null,
    int? DecadeFrom = null,
    int? DecadeTo = null,
    // Optional genre lane (a RiteGenres key, e.g. "black-metal"). Null = fully open, the default.
    // The tasting stays blind; the genre only narrows which bands the ring may draw from.
    string? Genre = null,
    // A raw lower-case tag substring, used DIRECTLY as the tag lane (bypassing the RiteGenres
    // catalogue) — how an arbitrary clicked tag scopes a blind rite. Takes precedence over Genre
    // when both are present. Null = no raw tag lane.
    string? GenreNeedle = null,
    // A raw theme key: a lyrical_themes substring when ThemeKind is "lyrical", or a TitleLexicon
    // theme id when ThemeKind is "mined". Null = no theme lane.
    string? ThemeNeedle = null,
    // "lyrical" or "mined" — which theme source ThemeNeedle scopes. Null when no theme scope.
    string? ThemeKind = null);

/// <summary>One genre lane offered in The Rite: its key (sent back on a serve) and display label.</summary>
public record RiteGenreDto(string Key, string Label);

/// <summary>
/// A rite served blind (SPEC §5.3). It carries NO name, genre, country or cover — only the
/// capability token, the risk, and the proxied audio URL. The origin preview URL never reaches
/// the client, so devtools cannot break the mechanic.
/// </summary>
public record ServedRiteDto(
    Guid Token,
    double RiskPercentile,
    string AudioUrl);

// ---------------------------------------------------------------------------
// Resolving a rite: Summon / Banish / Again (feature B13, C4)
// ---------------------------------------------------------------------------

/// <summary>Resolve a served rite. <see cref="Action"/> is <c>summon</c>, <c>banish</c> or <c>again</c>.</summary>
public record ResolveRequest(
    [Required] string Action);

/// <summary>
/// The outcome of resolving a rite. The band is revealed ONLY on <c>summon</c> (SPEC B13:
/// "it is only revealed if you like it"); banish and again keep it blind on purpose, because
/// the whole point of C3/C20 is that you judged without knowing what you rejected.
/// </summary>
public record ResolveResultDto(
    RiteState State,
    RiteRevealDto? Reveal);

/// <summary>
/// The reveal payload after a summon: the full artist plus the explanation of why it was served
/// (feature C4). It also carries everything feature C27 (guess the decade) scores against —
/// formed year, country and tags — and the user's <see cref="DepthScore"/> after this summon
/// (feature B15), so the reveal can show how far they have travelled.
/// </summary>
public record RiteRevealDto(
    ArtistDetailDto Artist,
    RiteExplanationDto Why,
    int DepthScore);

/// <summary>
/// "Why you were served this" (feature C4). Without it a strange recommender just looks broken.
/// Distance is the served artist's cosine distance to the current taste; the shared tags and
/// members connect it to the bands already in the user's grimoire.
/// </summary>
public record RiteExplanationDto(
    double Distance,
    IReadOnlyList<string> SharedTags,
    IReadOnlyList<string> SharedMembers);

/// <summary>An entry in the user's grimoire: a summoned band with when it was summoned.</summary>
public record GrimoireEntryDto(
    ArtistSummaryDto Artist,
    DateTimeOffset ResolvedAt);

// ---------------------------------------------------------------------------
// Crossed grimoires (feature C23)
// ---------------------------------------------------------------------------

/// <summary>The caller's own grimoire code — the opaque id a friend pastes to cross grimoires (C23).</summary>
public record GrimoireCodeDto(string Code);

/// <summary>
/// Two grimoires crossed (C23): the Dark Twin, but with a friend you named. <see cref="TheirsOnly"/>
/// is what they have summoned that you have not — the discoveries the comparison hands you;
/// <see cref="YoursOnly"/> is the reverse; <see cref="Shared"/> is common ground. Nothing is
/// invented: an empty grimoire on either side simply yields empty lists.
/// </summary>
public record CrossedGrimoiresDto(
    IReadOnlyList<ArtistSummaryDto> TheirsOnly,
    IReadOnlyList<ArtistSummaryDto> YoursOnly,
    IReadOnlyList<ArtistSummaryDto> Shared);
