using System.ComponentModel.DataAnnotations;

namespace Grimoire.Server.Dtos;

// ---------------------------------------------------------------------------
// The verdict game (the GAMES wave): "did your friend summon this band, or banish it?"
// ---------------------------------------------------------------------------

/// <summary>
/// Whether a verdict game can be dealt against a given friend right now, asked BEFORE offering to
/// play so the front can render the honest reason instead of a failed request.
/// <see cref="Reason"/> is a stable machine key the front translates (es/en) — never a server-side
/// sentence, which could not be localised. Null when <see cref="Playable"/> is true.
/// <see cref="VerdictsAvailable"/> is how many resolved rites the friend actually has: the honest
/// number behind the answer, shown as-is.
/// </summary>
public record VerdictGameAvailabilityDto(
    bool Playable,
    string? Reason,
    int VerdictsAvailable);

/// <summary>The body of "start a verdict game": which accepted friend's ear is being guessed.</summary>
public record StartVerdictGameRequest(
    [Required] Guid OpponentId);

/// <summary>
/// One round of a verdict game as the PLAYER is allowed to see it. This DTO is the blind contract,
/// and the reason the game cannot be won with devtools open:
///
/// <see cref="Artist"/>, <see cref="Truth"/>, <see cref="Answer"/> and <see cref="Correct"/> are all
/// null until the round is answered. The band's identity matters most of all — a friend's summoned
/// bands are already readable at <c>GET /api/friends/{id}/grimoire</c> (D57), so leaking an
/// unanswered round's artist id would hand over the answer directly: present in that list means
/// summoned, absent means banished. The player gets a capability token and a proxied audio URL, and
/// nothing else.
/// </summary>
public record GameRoundDto(
    Guid Token,
    int Ordinal,
    string AudioUrl,
    ArtistSummaryDto? Artist,
    string? Truth,
    string? Answer,
    bool? Correct);

/// <summary>
/// A verdict game: who is being guessed, the rounds (each blind until answered) and the running
/// score. <see cref="OpponentHandle"/> is null when the friend has not claimed a handle — an honest
/// gap, rendered as "your friend", never invented.
/// </summary>
public record VerdictGameDto(
    Guid Id,
    Guid OpponentId,
    string? OpponentHandle,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<GameRoundDto> Rounds,
    GameScoreDto Score);

/// <summary>
/// A game's score: how many rounds were answered right, how many were answered at all, and how many
/// were dealt. All three are shown — "3/5" during a game means something different from "3/5" at the
/// end, and collapsing them would lose that.
/// </summary>
public record GameScoreDto(
    int Correct,
    int Answered,
    int Total);

/// <summary>The body of answering a round: <c>summon</c> or <c>banish</c> — the player's read of their friend.</summary>
public record AnswerRoundRequest(
    [Required] string Verdict);

/// <summary>
/// The outcome of answering one round: whether the read was right, what the friend actually did, and
/// the band revealed at last. <see cref="Reveal"/> is the full band — the same payload The Rite's
/// summon reveals — so the round ends in a discovery rather than only a score.
/// <see cref="Finished"/> is true on the round that completes the game, which is when the opponent's
/// inbox learns it happened.
/// </summary>
public record AnswerRoundResultDto(
    bool Correct,
    string Truth,
    ArtistDetailDto? Reveal,
    GameScoreDto Score,
    bool Finished);

/// <summary>
/// A game in the history list, from the caller's point of view. <see cref="PlayedByMe"/> separates
/// "I guessed their ear" from "they guessed mine" — the two sides of the turn, both of which land in
/// this one list so the reply to a challenge is findable.
/// </summary>
public record VerdictGameSummaryDto(
    Guid Id,
    bool PlayedByMe,
    Guid OtherUserId,
    string? OtherHandle,
    string Status,
    DateTimeOffset CreatedAt,
    GameScoreDto Score);

/// <summary>
/// Whether the caller lets friends play the verdict game against their grimoire.
/// <see cref="OptIn"/> is null while they have never been asked — which is not the same as declining,
/// and is why the field is nullable. Both null and false refuse the game.
/// </summary>
public record VerdictGameConsentDto(
    bool? OptIn);

/// <summary>The body of setting that consent. Explicit both ways: there is no "un-ask".</summary>
public record SetVerdictGameConsentRequest(
    [Required] bool OptIn);
