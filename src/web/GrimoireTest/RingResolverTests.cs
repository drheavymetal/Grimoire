using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The slider → percentile → radius mapping that is the whole discovery engine after D26. These
/// bite: Comfort must resolve to nearer radii than Abyss, and the window must always be a genuine
/// sub-range of the distribution, never the whole thing.
/// </summary>
public class RingResolverTests
{
    // A spread-out sample so percentiles are easy to reason about.
    private static readonly double[] Sample =
        Enumerable.Range(0, 101).Select(i => i / 100.0).ToArray();

    [Fact]
    public void Percentiles_Comfort_SelectsTheLowEnd()
    {
        (double lo, double hi) = RingResolver.Percentiles(0.0, 0.20);

        Assert.Equal(0.0, lo, 6);
        Assert.Equal(0.20, hi, 6);
    }

    [Fact]
    public void Percentiles_Abyss_SelectsTheHighEnd()
    {
        (double lo, double hi) = RingResolver.Percentiles(1.0, 0.20);

        Assert.Equal(0.80, lo, 6);
        Assert.Equal(1.0, hi, 6);
    }

    [Fact]
    public void Percentiles_LoIsAlwaysBelowHi()
    {
        foreach (double comfort in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            (double lo, double hi) = RingResolver.Percentiles(comfort, 0.20);
            Assert.True(lo < hi, $"comfort={comfort}: lo {lo} must be below hi {hi}");
        }
    }

    [Fact]
    public void Percentiles_ClampsComfortToUnitInterval()
    {
        Assert.Equal(RingResolver.Percentiles(0.0, 0.20), RingResolver.Percentiles(-5.0, 0.20));
        Assert.Equal(RingResolver.Percentiles(1.0, 0.20), RingResolver.Percentiles(5.0, 0.20));
    }

    [Fact]
    public void ResolveRadii_ComfortIsNearerThanAbyss()
    {
        (double comfortLo, double comfortHi) = RingResolver.ResolveRadii(0.0, Sample, 0.20);
        (double abyssLo, double abyssHi) = RingResolver.ResolveRadii(1.0, Sample, 0.20);

        // The abyss ring sits strictly beyond the comfort ring — the slider actually moves.
        // (Swap the assertion to comfort > abyss and it fails: direction is load-bearing.)
        Assert.True(comfortHi < abyssLo, $"comfort ring [{comfortLo},{comfortHi}] must be nearer than abyss [{abyssLo},{abyssHi}]");
    }

    [Fact]
    public void SafeRadius_IsThePercentileOfTheRepulsionSample()
    {
        double safe = RingResolver.SafeRadius(Sample, 0.20);

        Assert.Equal(0.20, safe, 6);
    }

    [Fact]
    public void Percentiles_RejectsDegenerateWindowWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RingResolver.Percentiles(0.5, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RingResolver.Percentiles(0.5, 1.0));
    }
}
