using Grimoire.Library.Models;
using Grimoire.Server.Dtos;

namespace Grimoire.Server.Services;

/// <summary>
/// Turns game rows into what a player is allowed to see. This is the ONE place the blind/revealed
/// decision is made, and it is pure so a test can hold it to that without a database or a request.
///
/// The rule is single and mechanical: a round shows nothing but its token, its position and its
/// audio until <see cref="GameRound.AnsweredAt"/> is set. Scattering that condition across a
/// controller is how a later edit reveals a band by accident — the leak would be a 200 with the
/// right shape, invisible to every test that only checks status codes.
/// </summary>
public static class GameView
{
    /// <summary>
    /// One round, filtered to what its player may see. An unanswered round yields the band, the
    /// truth, the answer and the correctness ALL null — a player who reads the raw response learns
    /// only that a round exists and where its audio is.
    /// </summary>
    public static GameRoundDto Round(GameRound round, string audioUrl, ArtistSummaryDto? artist)
    {
        ArgumentNullException.ThrowIfNull(round);

        if (round.AnsweredAt is null)
        {
            return new GameRoundDto(round.Id, round.Ordinal, audioUrl, null, null, null, null);
        }

        return new GameRoundDto(
            round.Id,
            round.Ordinal,
            audioUrl,
            artist,
            round.Truth?.ToString(),
            round.Answer?.ToString(),
            round.Correct);
    }

    /// <summary>Whether a round's band may be named to its player yet — the same gate <see cref="Round"/> applies.</summary>
    public static bool IsRevealed(GameRound round)
    {
        ArgumentNullException.ThrowIfNull(round);

        return round.AnsweredAt is not null;
    }

    /// <summary>
    /// The score over a game's rounds: right answers, answers given, rounds dealt. Counts only what
    /// was actually answered — an unanswered round is not a wrong one, it is an unfinished one.
    /// </summary>
    public static GameScoreDto Score(IReadOnlyCollection<GameRound> rounds)
    {
        ArgumentNullException.ThrowIfNull(rounds);

        return new GameScoreDto(
            rounds.Count(r => r.Correct == true),
            rounds.Count(r => r.AnsweredAt is not null),
            rounds.Count);
    }

    /// <summary>
    /// Parses a player's answer. Only the two verdicts a rite can hold are accepted: <c>summon</c>
    /// and <c>banish</c> (the same words The Rite's own buttons use — <c>again</c> is a skip, not a
    /// verdict, so it is not an answer here).
    /// </summary>
    public static bool TryParseVerdict(string? verdict, out RiteState state)
    {
        switch (verdict?.Trim().ToLowerInvariant())
        {
            case "summon":
            case "summoned":
                state = RiteState.Summoned;
                return true;

            case "banish":
            case "banished":
                state = RiteState.Banished;
                return true;

            default:
                state = default;
                return false;
        }
    }

    /// <summary>The stable machine key the front translates for a blocked game (never a server sentence).</summary>
    public static string ReasonKey(VerdictGameBlocker blocker)
    {
        return blocker switch
        {
            VerdictGameBlocker.TooFewVerdicts => "too-few-verdicts",
            VerdictGameBlocker.NoBanishments => "no-banishments",
            VerdictGameBlocker.NoSummons => "no-summons",
            VerdictGameBlocker.NotEnoughAudible => "not-enough-audible",
            _ => "none",
        };
    }

    // -----------------------------------------------------------------------
    // Guess the band (D67) — the same rule, one turn stricter
    // -----------------------------------------------------------------------

    /// <summary>
    /// One guess-the-band round, filtered to what its player may see. The gate is the same single
    /// mechanical condition the verdict game uses, and it lives here for the same reason: scattered
    /// across a controller, a later edit reveals a band by accident, and the leak is a 200 with the
    /// right shape that no status-code test would ever catch.
    ///
    /// <para>
    /// What differs is the stakes. In the verdict game the artist id is a strong hint — it lets you
    /// look the band up in your friend's public grimoire. Here it is the ANSWER, exactly and
    /// literally. So an unanswered round yields the band and the correctness null, and the only names
    /// that go out are the multiple choice's four — among which the true one is placed by a hash of the
    /// round id, indistinguishable from its three decoys (<see cref="GuessGamePool.Choices"/>).
    /// </para>
    /// </summary>
    /// <param name="choices">Normal's four names, already shuffled. Null in Hard — nothing is offered there.</param>
    /// <param name="artist">The band. Passed only for an answered round; the caller does not even load it otherwise.</param>
    public static GuessRoundDto GuessRound(
        GameRound round,
        string audioUrl,
        IReadOnlyList<GuessChoiceDto>? choices,
        ArtistSummaryDto? artist)
    {
        ArgumentNullException.ThrowIfNull(round);

        if (round.AnsweredAt is null)
        {
            return new GuessRoundDto(round.Id, round.Ordinal, audioUrl, choices, null, null);
        }

        return new GuessRoundDto(round.Id, round.Ordinal, audioUrl, choices, artist, round.Correct);
    }

    /// <summary>
    /// The score over a guess game's rounds, in rounds and in points. The points are computed here,
    /// once, from the difficulty the game was DEALT at — never from a difficulty read back at display
    /// time, which is the same discipline <see cref="GameRound.Truth"/> follows: a score that can move
    /// after the fact is not a score.
    /// </summary>
    public static GuessScoreDto GuessScore(IReadOnlyCollection<GameRound> rounds, GameDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(rounds);

        int correct = rounds.Count(r => r.Correct == true);

        return new GuessScoreDto(
            correct,
            rounds.Count(r => r.AnsweredAt is not null),
            rounds.Count,
            GuessGamePool.Points(correct, difficulty),
            GuessGamePool.PointsPerRound(difficulty));
    }

    /// <summary>Parses the difficulty a player asked for. Unknown words are refused, never defaulted:
    /// silently dealing Normal to somebody who asked for Hard would misprice their whole game.</summary>
    public static bool TryParseDifficulty(string? difficulty, out GameDifficulty parsed)
    {
        switch (difficulty?.Trim().ToLowerInvariant())
        {
            case "normal":
                parsed = GameDifficulty.Normal;
                return true;

            case "hard":
                parsed = GameDifficulty.Hard;
                return true;

            default:
                parsed = default;
                return false;
        }
    }

    /// <summary>The stable machine key the front translates for a blocked guess game.</summary>
    public static string ReasonKey(GuessGameBlocker blocker)
    {
        return blocker switch
        {
            GuessGameBlocker.TooFewSummons => "too-few-summons",
            GuessGameBlocker.NotEnoughChoices => "not-enough-choices",
            GuessGameBlocker.NotEnoughAudible => "not-enough-audible",
            _ => "none",
        };
    }
}
