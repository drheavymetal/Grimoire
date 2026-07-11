using System.ComponentModel.DataAnnotations;

namespace Grimoire.Server.Dtos;

// ---------------------------------------------------------------------------
// The blind duel (feature C2, DECISIONS D16)
// ---------------------------------------------------------------------------

/// <summary>
/// A request to start a blind duel. Like a serve, <see cref="Comfort"/> is the Comfort ↔ Abyss
/// slider in [0, 1] and the optional hard filters are decade and country only (feature C13) — both
/// bands are drawn from the same ring, so the duel compares like with like.
/// </summary>
public record DuelRequest(
    double Comfort = 0.5,
    string? Country = null,
    int? DecadeFrom = null,
    int? DecadeTo = null);

/// <summary>
/// One side of a duel, served blind: only the capability token and the proxied audio URL. It
/// carries NO name, genre, country or cover — the whole point is to judge by ear (SPEC §5.3).
/// </summary>
public record DuelSideDto(
    Guid Token,
    string AudioUrl);

/// <summary>The two blind bands of a duel. The user listens to both and picks one.</summary>
public record DuelServedDto(
    DuelSideDto Left,
    DuelSideDto Right);

/// <summary>
/// A resolved duel: the winner the user preferred and the loser they passed over. Both must be
/// the caller's own unresolved served rites (from a duel they just started).
/// </summary>
public record DuelResolveRequest(
    [Required] Guid WinnerToken,
    [Required] Guid LoserToken);

/// <summary>
/// The outcome of a duel: the winner is revealed (it entered the grimoire), and the taste vector
/// moved toward it and away from the loser (Bradley-Terry, DECISIONS D16). The reveal reuses the
/// summon reveal shape, so it carries the "why" explanation and the depth score too.
/// </summary>
public record DuelResultDto(
    RiteRevealDto Reveal);

// ---------------------------------------------------------------------------
// Guess the decade (feature C27)
// ---------------------------------------------------------------------------

/// <summary>
/// A request to serve one blind band for the decade game. <see cref="Comfort"/> is the same
/// Comfort ↔ Abyss slider; the pool is narrowed to bands that can be scored (formed year, country
/// and at least one tag), so every bet is judged against a real value.
/// </summary>
public record DecadeServeRequest(double Comfort = 0.5);

/// <summary>One blind band for the decade game: the capability token and the proxied audio URL.</summary>
public record DecadeServedDto(
    Guid Token,
    string AudioUrl);

/// <summary>
/// The player's bet for a round of the decade game (feature C27): a decade (any year in it, e.g.
/// 1985 for the 1980s), a country code, and a subgenre. Country and subgenre are optional — a
/// player may bet only what they are sure of.
/// </summary>
public record DecadeGuessRequest(
    int Decade,
    string? Country = null,
    string? Subgenre = null);

/// <summary>One scored dimension of a decade guess: what was bet, the truth, the outcome and points.</summary>
public record DecadeDimensionDto(
    string Guess,
    string Actual,
    string Outcome,
    int Points);

/// <summary>
/// The reveal and score of a decade round (feature C27): the full band (so the UI can develop the
/// name and link to the ficha), the three scored dimensions, and the round total. The scoreboard is
/// accumulated in the session by the front — no persistence, no migration.
/// </summary>
public record DecadeScoreDto(
    ArtistDetailDto Artist,
    DecadeDimensionDto Decade,
    DecadeDimensionDto Country,
    DecadeDimensionDto Subgenre,
    int TotalPoints,
    int MaxPoints);
