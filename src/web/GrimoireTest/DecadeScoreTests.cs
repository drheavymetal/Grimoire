using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// Scoring for "guess the decade" (feature C27). These bite on the boundaries — an exact decade, one
/// decade off, two decades off; case and whitespace in the country; a subgenre that is a token of a
/// tag versus one that is not — and on the honest empty cases (a missing truth is a Miss, never a
/// throw, never invented data).
/// </summary>
public class DecadeScoreTests
{
    private static RoundScore Score(int decade, string? country, string? subgenre, int? formedYear, string? actualCountry, params string[] tags)
    {
        return DecadeScore.Score(
            new DecadeGuess(decade, country, subgenre),
            new DecadeTruth(formedYear, actualCountry, tags));
    }

    // -----------------------------------------------------------------------
    // The decade
    // -----------------------------------------------------------------------

    [Fact]
    public void Decade_ExactDecade_IsAHit()
    {
        RoundScore s = Score(1985, null, null, 1987, "NO");
        Assert.Equal(GuessOutcome.Hit, s.Decade.Outcome);
        Assert.Equal(DecadeScore.DecadeHitPoints, s.Decade.Points);
    }

    [Fact]
    public void Decade_OneDecadeOff_IsClose()
    {
        // Bet the 1990s, band formed 1987 (1980s): one decade off → Close.
        RoundScore s = Score(1994, null, null, 1987, "NO");
        Assert.Equal(GuessOutcome.Close, s.Decade.Outcome);
        Assert.Equal(DecadeScore.DecadeClosePoints, s.Decade.Points);
    }

    [Fact]
    public void Decade_TwoDecadesOff_IsAMiss()
    {
        RoundScore s = Score(2005, null, null, 1987, "NO");
        Assert.Equal(GuessOutcome.Miss, s.Decade.Outcome);
        Assert.Equal(0, s.Decade.Points);
    }

    [Fact]
    public void Decade_DecadeBoundary_YearsInTheSameDecadeAllHit()
    {
        // 1980 and 1989 are both the 1980s; a bet of 1980 must hit a band formed in 1989.
        Assert.Equal(GuessOutcome.Hit, Score(1980, null, null, 1989, "US").Decade.Outcome);
        // 1990 crosses into the next decade → Close, not Hit.
        Assert.Equal(GuessOutcome.Close, Score(1990, null, null, 1989, "US").Decade.Outcome);
    }

    [Fact]
    public void Decade_NullFormedYear_IsAMiss_NotAThrow()
    {
        RoundScore s = Score(1985, null, null, null, "NO");
        Assert.Equal(GuessOutcome.Miss, s.Decade.Outcome);
        Assert.Equal(0, s.Decade.Points);
    }

    [Fact]
    public void DecadeOf_NormalisesToStartOfDecade()
    {
        Assert.Equal(1980, DecadeScore.DecadeOf(1987));
        Assert.Equal(1990, DecadeScore.DecadeOf(1990));
        Assert.Equal(2000, DecadeScore.DecadeOf(2009));
    }

    // -----------------------------------------------------------------------
    // The country
    // -----------------------------------------------------------------------

    [Fact]
    public void Country_ExactMatch_IsAHit_CaseAndWhitespaceInsensitive()
    {
        RoundScore s = Score(1985, " no ", null, 1987, "NO");
        Assert.Equal(GuessOutcome.Hit, s.Country.Outcome);
        Assert.Equal(DecadeScore.CountryHitPoints, s.Country.Points);
    }

    [Fact]
    public void Country_Wrong_IsAMiss()
    {
        RoundScore s = Score(1985, "SE", null, 1987, "NO");
        Assert.Equal(GuessOutcome.Miss, s.Country.Outcome);
        Assert.Equal(0, s.Country.Points);
    }

    [Fact]
    public void Country_NullOrEmptyGuess_IsAMiss_NoPointsForNotBetting()
    {
        Assert.Equal(GuessOutcome.Miss, Score(1985, null, null, 1987, "NO").Country.Outcome);
        Assert.Equal(GuessOutcome.Miss, Score(1985, "   ", null, 1987, "NO").Country.Outcome);
    }

    [Fact]
    public void Country_NullTruth_IsAMiss_NotAThrow()
    {
        RoundScore s = Score(1985, "NO", null, 1987, null);
        Assert.Equal(GuessOutcome.Miss, s.Country.Outcome);
    }

    // -----------------------------------------------------------------------
    // The subgenre
    // -----------------------------------------------------------------------

    [Fact]
    public void Subgenre_ExactTag_IsAHit()
    {
        RoundScore s = Score(1985, null, "black metal", 1987, "NO", "black metal", "norwegian");
        Assert.Equal(GuessOutcome.Hit, s.Subgenre.Outcome);
        Assert.Equal(DecadeScore.SubgenreHitPoints, s.Subgenre.Points);
    }

    [Fact]
    public void Subgenre_TokenOfATag_IsAHit()
    {
        // "black" is a word inside the tag "black metal" → a hit (case-insensitive).
        Assert.Equal(GuessOutcome.Hit, Score(1985, null, "Black", 1987, "NO", "black metal").Subgenre.Outcome);
    }

    [Fact]
    public void Subgenre_GuessContainsTheTag_IsAHit()
    {
        // The band is tagged "black metal"; a bet of "atmospheric black metal" contains it → hit.
        Assert.Equal(GuessOutcome.Hit, Score(1985, null, "atmospheric black metal", 1987, "NO", "black metal").Subgenre.Outcome);
    }

    [Fact]
    public void Subgenre_Unrelated_IsAMiss()
    {
        RoundScore s = Score(1985, null, "grindcore", 1987, "NO", "black metal", "viking");
        Assert.Equal(GuessOutcome.Miss, s.Subgenre.Outcome);
        Assert.Equal(0, s.Subgenre.Points);
    }

    [Fact]
    public void Subgenre_EmptyGuessOrNoTags_IsAMiss()
    {
        Assert.Equal(GuessOutcome.Miss, Score(1985, null, null, 1987, "NO", "black metal").Subgenre.Outcome);
        Assert.Equal(GuessOutcome.Miss, Score(1985, null, "black metal", 1987, "NO").Subgenre.Outcome); // no tags
    }

    // -----------------------------------------------------------------------
    // The round total
    // -----------------------------------------------------------------------

    [Fact]
    public void Total_AllThreeRight_IsTheMax()
    {
        RoundScore s = Score(1985, "NO", "black metal", 1987, "NO", "black metal");
        Assert.Equal(RoundScore.MaxPoints, s.Total);
        Assert.Equal(DecadeScore.DecadeHitPoints + DecadeScore.CountryHitPoints + DecadeScore.SubgenreHitPoints, s.Total);
    }

    [Fact]
    public void Total_AllWrong_IsZero()
    {
        RoundScore s = Score(2010, "US", "grindcore", 1987, "NO", "black metal");
        Assert.Equal(0, s.Total);
    }

    [Fact]
    public void Total_CloseDecadePlusCountry_SumsThePartials()
    {
        // 1990s bet on an 1987 band (Close, 1) + right country (1) + wrong subgenre (0) = 2.
        RoundScore s = Score(1994, "NO", "grindcore", 1987, "NO", "black metal");
        Assert.Equal(DecadeScore.DecadeClosePoints + DecadeScore.CountryHitPoints, s.Total);
    }
}
