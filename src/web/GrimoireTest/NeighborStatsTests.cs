using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class NeighborStatsTests
{
    [Fact]
    public void Percentile_Median_OfOddSample()
    {
        Assert.Equal(3.0, NeighborStats.Percentile([1.0, 2.0, 3.0, 4.0, 5.0], 0.5), 6);
    }

    [Fact]
    public void Percentile_Interpolates()
    {
        // R-7 definition: p90 of 0..10 (eleven points) is exactly 9.
        double[] values = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        Assert.Equal(9.0, NeighborStats.Percentile(values, 0.9), 6);
    }

    [Fact]
    public void Percentile_UnsortedInput_StillCorrect()
    {
        Assert.Equal(2.0, NeighborStats.Percentile([5.0, 1.0, 2.0, 4.0, 3.0], 0.25), 6);
    }

    [Fact]
    public void Percentile_EmptySample_Throws()
    {
        Assert.Throws<ArgumentException>(() => NeighborStats.Percentile([], 0.5));
    }

    [Fact]
    public void SpreadOf_DivergentDistances_IsHealthy()
    {
        double[] dists = [0.05, 0.10, 0.20, 0.35, 0.50, 0.70, 0.90];
        NeighborStats.Spread spread = NeighborStats.SpreadOf(dists);

        Assert.True(spread.P10 < spread.P50);
        Assert.True(spread.P50 < spread.P90);
        Assert.False(spread.IsDegenerate());
    }

    [Fact]
    public void SpreadOf_NearlyEqualDistances_IsDegenerate()
    {
        // The exact failure D26 exists to prevent: every neighbour at the same distance.
        double[] dists = [0.249, 0.250, 0.250, 0.251, 0.250, 0.250, 0.249];
        NeighborStats.Spread spread = NeighborStats.SpreadOf(dists);

        Assert.True(spread.IsDegenerate());
    }
}
