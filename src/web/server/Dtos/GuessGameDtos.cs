using System.ComponentModel.DataAnnotations;

namespace Grimoire.Server.Dtos;

// ---------------------------------------------------------------------------
// Guess the band (D67): "you loved it blind — do you even know who it is?"
//
// Played over the player's OWN summons, which is the bound that makes it a Grimoire game instead of a
// trivia quiz. A general name quiz rewards whoever already knows the canon and so inverts the Ranks
// pillar (D43/D66); over your own grimoire the question reverses, and the answer is one you already
// gave with your ears.
// ---------------------------------------------------------------------------

/// <summary>
/// Whether the caller's own grimoire can make a game at a given difficulty, and if not, why. Asked
/// BEFORE offering to play, so an unplayable grimoire renders a designed sentence about real data
/// rather than a greyed-out button or a failed request. <see cref="Reason"/> is a stable machine key
/// the front translates — never a server-side sentence, which could not be localised.
/// </summary>
/// <param name="Playable">Whether a deal would succeed right now.</param>
/// <param name="Reason">The blocker's key, or null when playable.</param>
/// <param name="SummonsAvailable">
/// How many bands the caller has actually summoned: the honest number behind the answer, shown as-is.
/// It is also the number that grows by playing The Rite, which is the only way this game fills up.
/// </param>
public record GuessGameAvailabilityDto(
    bool Playable,
    string? Reason,
    int SummonsAvailable);

/// <summary>
/// The body of "deal me a game": how hard, and who — if anyone — the score is being sent to.
/// </summary>
/// <param name="Difficulty"><c>normal</c> (four names) or <c>hard</c> (type it, worth more).</param>
/// <param name="OpponentId">
/// The friend to challenge, or null for the solo game. Null is the ordinary case and costs nothing to
/// support: the column has always been nullable, precisely so this mode would not need a table (D66).
/// Note what a challenge is NOT: the opponent's grimoire is never read, and never touched. Each side
/// plays their own summons and only the scores meet, which is what makes the match fair when two
/// grimoires have nothing in common — and today, across all of production, they have nothing in common.
/// </param>
public record StartGuessGameRequest(
    [Required] string Difficulty,
    Guid? OpponentId);

/// <summary>
/// One name on offer in <see cref="Grimoire.Library.Models.GameDifficulty.Normal"/>. Four of these
/// reach the player, one of them true, and they are deliberately IDENTICAL in shape: an id and a name,
/// nothing else, in an order that is a pure function of the round's id.
///
/// <para>
/// This DTO is the reason the multiple choice can be sent at all. Any field that only the answer could
/// fill — a rank, a country, a year, a null where the others have a value — would let a player read
/// the answer off the payload instead of hearing it, and it would do so silently, in a 200 of exactly
/// the right shape. So there are no such fields, and there is nothing here to add one to.
/// </para>
/// </summary>
public record GuessChoiceDto(
    Guid ArtistId,
    string Name);

/// <summary>
/// One round as the player is allowed to see it — the blind contract of this game, and the reason it
/// cannot be won with devtools open.
///
/// <para>
/// The band is the ANSWER here, which makes this stricter than the verdict game: there, leaking the
/// artist id let a player look the band up in a friend's public grimoire; here, the id IS the thing
/// being asked for. So until <see cref="Correct"/> is set, <see cref="Artist"/> is null and the only
/// identity on the wire is whatever <see cref="Choices"/> carries — four names, one true, shuffled.
/// </para>
/// <para>
/// <see cref="Choices"/> is null in Hard: there is nothing to choose from, the player types the name.
/// A null and an empty list would mean different things, so it is null, and the front reads it as
/// "this is the typing round".
/// </para>
/// </summary>
public record GuessRoundDto(
    Guid Token,
    int Ordinal,
    string AudioUrl,
    IReadOnlyList<GuessChoiceDto>? Choices,
    ArtistSummaryDto? Artist,
    bool? Correct);

/// <summary>
/// A guess-the-band game: how hard it was dealt, who the score goes to (null when solo), the rounds
/// and the running score.
/// </summary>
/// <param name="OpponentId">The challenged friend, or null for a solo game. Their grimoire was not read.</param>
/// <param name="OpponentHandle">Their handle, or null when they have not claimed one — an honest gap, never invented.</param>
public record GuessGameDto(
    Guid Id,
    string Difficulty,
    Guid? OpponentId,
    string? OpponentHandle,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<GuessRoundDto> Rounds,
    GuessScoreDto Score);

/// <summary>
/// A guess game's score. The three counts mean what they do in the verdict game — right, answered,
/// dealt, kept apart because an unanswered round is not a wrong one — plus the two that only exist
/// here: <see cref="Points"/> and the rate it is earned at.
/// </summary>
/// <param name="Points">
/// What the round count is actually worth. Carried rather than left for the front to multiply, because
/// two clients that each do their own arithmetic are two clients that will eventually disagree about a
/// score two friends are comparing.
/// </param>
/// <param name="PointsPerRound">The exchange rate this game was dealt at, so the number above can be read.</param>
public record GuessScoreDto(
    int Correct,
    int Answered,
    int Total,
    int Points,
    int PointsPerRound);

/// <summary>
/// The body of answering a round. Exactly one of the two is used, and WHICH one is decided by the
/// game's stored difficulty rather than by what the client chose to send: a Hard round answered with
/// an <see cref="ArtistId"/> would be a multiple choice with the choices hidden, and the point of Hard
/// is that there is nothing to pick from.
/// </summary>
/// <param name="ArtistId">Normal: which of the four names. Must be one of the round's actual choices.</param>
/// <param name="Name">Hard: the band's name as typed, judged by <c>GuessMatch</c> — accents forgiven, other bands not.</param>
public record AnswerGuessRoundRequest(
    Guid? ArtistId,
    string? Name);

/// <summary>
/// The outcome of one round: whether it was right, and the band at last. <see cref="Reveal"/> is the
/// full band — the same payload The Rite's summon reveals — so a round ends in a discovery (or a
/// re-introduction to something you already loved and could not name) rather than only a tick.
/// </summary>
/// <param name="Finished">True on the round that completes the game — when a challenged friend's inbox learns of it.</param>
public record AnswerGuessRoundResultDto(
    bool Correct,
    ArtistDetailDto? Reveal,
    GuessScoreDto Score,
    bool Finished);

/// <summary>
/// A game in the history list, from the caller's point of view. Both sides of the turn land here:
/// <see cref="PlayedByMe"/> false is a friend's challenge — their score, over THEIR grimoire, waiting
/// to be answered by playing your own.
/// </summary>
/// <param name="OtherUserId">The other side, or null when the game was solo.</param>
public record GuessGameSummaryDto(
    Guid Id,
    bool PlayedByMe,
    string Difficulty,
    Guid? OtherUserId,
    string? OtherHandle,
    string Status,
    DateTimeOffset CreatedAt,
    GuessScoreDto Score);
