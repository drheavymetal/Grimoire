using Grimoire.Library.Models;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The Depth Score (feature B15): how far the user has travelled, summed over summoned bands, more
/// points for rarer finds. These bite on the tier ordering and on the null-rank case — a band with
/// no rank must score nothing, never an invented value.
/// </summary>
public class DepthScoreTests
{
    [Theory]
    [InlineData(Rank.Nameless, 5)]
    [InlineData(Rank.Forgotten, 4)]
    [InlineData(Rank.Hidden, 3)]
    [InlineData(Rank.Obscure, 2)]
    [InlineData(Rank.Known, 1)]
    public void Points_RarerTiersAreWorthMore(Rank rank, int expected)
    {
        Assert.Equal(expected, DepthScore.Points(rank));
    }

    [Fact]
    public void Points_MonotonicByRarity()
    {
        // Nameless > Forgotten > Hidden > Obscure > Known — the whole point of the score.
        Assert.True(DepthScore.Points(Rank.Nameless) > DepthScore.Points(Rank.Forgotten));
        Assert.True(DepthScore.Points(Rank.Forgotten) > DepthScore.Points(Rank.Hidden));
        Assert.True(DepthScore.Points(Rank.Hidden) > DepthScore.Points(Rank.Obscure));
        Assert.True(DepthScore.Points(Rank.Obscure) > DepthScore.Points(Rank.Known));
    }

    [Fact]
    public void Points_NullRank_ScoresZero()
    {
        // A band with unknown listeners (null rank) contributes nothing. No rank is invented.
        Assert.Equal(0, DepthScore.Points(null));
    }

    [Fact]
    public void Compute_Empty_IsZero()
    {
        Assert.Equal(0, DepthScore.Compute([]));
    }

    [Fact]
    public void Compute_SumsPerBandPoints()
    {
        // Nameless(5) + Hidden(3) + Known(1) = 9.
        Rank?[] ranks = [Rank.Nameless, Rank.Hidden, Rank.Known];

        Assert.Equal(9, DepthScore.Compute(ranks));
    }

    [Fact]
    public void Compute_NullRanksAddNothing_ButRealOnesStillCount()
    {
        // Two null-rank bands and one Forgotten(4): the nulls add nothing, the real one counts.
        // If null ever scored, this total would exceed 4 — the guard against invented data.
        Rank?[] ranks = [null, Rank.Forgotten, null];

        Assert.Equal(4, DepthScore.Compute(ranks));
    }

    [Fact]
    public void Compute_AllNull_IsZero_DegradesWithDignity()
    {
        // While Last.fm is unkeyed every rank is null, so the score stays 0 for everyone — the
        // engine degrades with dignity instead of fabricating depth.
        Rank?[] ranks = [null, null, null];

        Assert.Equal(0, DepthScore.Compute(ranks));
    }
}
