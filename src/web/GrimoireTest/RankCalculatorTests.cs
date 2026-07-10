using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class RankCalculatorTests
{
    [Theory]
    [InlineData(499, Rank.Nameless)]
    [InlineData(500, Rank.Forgotten)]
    [InlineData(4_999, Rank.Forgotten)]
    [InlineData(5_000, Rank.Hidden)]
    [InlineData(49_999, Rank.Hidden)]
    [InlineData(50_000, Rank.Obscure)]
    [InlineData(499_999, Rank.Obscure)]
    [InlineData(500_000, Rank.Obscure)]
    [InlineData(500_001, Rank.Known)]
    [InlineData(0, Rank.Nameless)]
    public void FromListeners_MapsBoundaries(int listeners, Rank expected)
    {
        Assert.Equal(expected, RankCalculator.FromListeners(listeners));
    }

    [Fact]
    public void FromListeners_NullWhenUnknown()
    {
        Assert.Null(RankCalculator.FromListeners(null));
    }
}
