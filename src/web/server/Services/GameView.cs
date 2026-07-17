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
}
