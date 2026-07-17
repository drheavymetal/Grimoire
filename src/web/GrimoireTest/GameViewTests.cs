using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// What a player is allowed to see. These are the anti-cheat tests: the verdict game is won by
/// knowing your friend's ear, and it would be trivially won instead by reading the serve response if
/// that response carried the band or the answer. The filter has one home (<see cref="GameView"/>) so
/// it can be held to that here, without a database.
/// </summary>
public class GameViewTests
{
    private const string AudioUrl = "https://grimoire.test/api/games/rounds/x/audio";

    private static readonly ArtistSummaryDto Band =
        new(Guid.NewGuid(), "Blood Incantation", "US", 2011, Rank.Obscure);

    private static GameRound Unanswered()
    {
        return new GameRound
        {
            Id = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            Ordinal = 0,
            ArtistId = Band.Id,
            Truth = RiteState.Banished,
        };
    }

    private static GameRound Answered(RiteState truth, RiteState answer)
    {
        return new GameRound
        {
            Id = Guid.NewGuid(),
            GameId = Guid.NewGuid(),
            Ordinal = 0,
            ArtistId = Band.Id,
            Truth = truth,
            Answer = answer,
            Correct = truth == answer,
            AnsweredAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// The whole mechanic in one assertion. An unanswered round must carry NOTHING but its token, its
    /// position and its audio — no truth, no answer, no correctness, and above all no band.
    /// </summary>
    [Fact]
    public void Round_Unanswered_RevealsNothingButTheAudio()
    {
        GameRound round = Unanswered();

        GameRoundDto dto = GameView.Round(round, AudioUrl, Band);

        Assert.Equal(round.Id, dto.Token);
        Assert.Equal(AudioUrl, dto.AudioUrl);
        Assert.Null(dto.Truth);
        Assert.Null(dto.Answer);
        Assert.Null(dto.Correct);
        Assert.Null(dto.Artist);
    }

    /// <summary>
    /// The sharpest leak, called out on its own because it is not obvious: a friend's SUMMONED bands
    /// are already readable at GET /api/friends/{id}/grimoire (D57). So an unanswered round's artist
    /// id is the answer itself — present in that list means summoned, absent means banished. Handing
    /// the band over even without the verdict would hand the game over.
    /// </summary>
    [Fact]
    public void Round_Unanswered_NeverNamesTheBand_EvenWhenTheCallerHasIt()
    {
        GameRoundDto dto = GameView.Round(Unanswered(), AudioUrl, Band);

        Assert.Null(dto.Artist);
    }

    [Fact]
    public void Round_Answered_RevealsTheBandTheTruthAndTheVerdict()
    {
        GameRound round = Answered(RiteState.Banished, RiteState.Banished);

        GameRoundDto dto = GameView.Round(round, AudioUrl, Band);

        Assert.Equal("Banished", dto.Truth);
        Assert.Equal("Banished", dto.Answer);
        Assert.True(dto.Correct);
        Assert.Equal(Band, dto.Artist);
    }

    [Fact]
    public void Round_Answered_MarksAWrongReadWrong()
    {
        GameRoundDto dto = GameView.Round(Answered(RiteState.Banished, RiteState.Summoned), AudioUrl, Band);

        Assert.False(dto.Correct);
        Assert.Equal("Banished", dto.Truth);
        Assert.Equal("Summoned", dto.Answer);
    }

    [Fact]
    public void IsRevealed_TracksWhetherTheRoundWasAnswered()
    {
        Assert.False(GameView.IsRevealed(Unanswered()));
        Assert.True(GameView.IsRevealed(Answered(RiteState.Summoned, RiteState.Summoned)));
    }

    /// <summary>
    /// An unanswered round is not a wrong one. A game abandoned at 2/5 must not read as 2 correct out
    /// of 5 attempts — the three unplayed rounds were never guessed at.
    /// </summary>
    [Fact]
    public void Score_CountsRightAnswersAnswersAndRoundsSeparately()
    {
        List<GameRound> rounds =
        [
            Answered(RiteState.Summoned, RiteState.Summoned),
            Answered(RiteState.Banished, RiteState.Summoned),
            Unanswered(),
            Unanswered(),
        ];

        GameScoreDto score = GameView.Score(rounds);

        Assert.Equal(1, score.Correct);
        Assert.Equal(2, score.Answered);
        Assert.Equal(4, score.Total);
    }

    [Fact]
    public void Score_OfAFreshDeal_IsAllZeroesOverTheRounds()
    {
        GameScoreDto score = GameView.Score([Unanswered(), Unanswered(), Unanswered()]);

        Assert.Equal(0, score.Correct);
        Assert.Equal(0, score.Answered);
        Assert.Equal(3, score.Total);
    }

    [Theory]
    [InlineData("summon", RiteState.Summoned)]
    [InlineData("Summoned", RiteState.Summoned)]
    [InlineData("  BANISH ", RiteState.Banished)]
    [InlineData("banished", RiteState.Banished)]
    public void TryParseVerdict_AcceptsTheTwoVerdicts(string input, RiteState expected)
    {
        Assert.True(GameView.TryParseVerdict(input, out RiteState state));
        Assert.Equal(expected, state);
    }

    /// <summary>
    /// "Again" is a skip, not a verdict — the pool excludes it, so it cannot be an answer either.
    /// Accepting it would let a player answer with a value no round can ever be true for.
    /// </summary>
    [Theory]
    [InlineData("again")]
    [InlineData("served")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("maybe")]
    public void TryParseVerdict_RejectsAnythingElse(string? input)
    {
        Assert.False(GameView.TryParseVerdict(input, out _));
    }

    [Fact]
    public void ReasonKey_GivesTheFrontAStableKeyPerBlocker()
    {
        Assert.Equal("too-few-verdicts", GameView.ReasonKey(VerdictGameBlocker.TooFewVerdicts));
        Assert.Equal("no-banishments", GameView.ReasonKey(VerdictGameBlocker.NoBanishments));
        Assert.Equal("no-summons", GameView.ReasonKey(VerdictGameBlocker.NoSummons));
        Assert.Equal("not-enough-audible", GameView.ReasonKey(VerdictGameBlocker.NotEnoughAudible));
    }
}
