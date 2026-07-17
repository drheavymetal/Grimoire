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

    // --- The reach curve (D68) ---
    //
    // The tests above pass under BOTH the linear map and the curved one, because they only pin the
    // endpoints — which are fixed points under any positive curve. That is exactly how the linear
    // map shipped a midpoint that served random bands for months: nothing tested the middle. These
    // pin the middle.

    [Fact]
    public void Percentiles_MidSlider_StaysOutOfTheCorpusMedian()
    {
        // The whole point of D68. Under the linear map the midpoint landed at [0.40, 0.60] — a
        // window straddling the corpus median, which is by definition the typical band: measured
        // 52.7% on-target against a 50.5% coin flip. The curve must pull it clear of the median.
        (double lo, double hi) = RingResolver.Percentiles(0.5, 0.20);

        Assert.Equal(0.20, lo, 6);
        Assert.Equal(0.40, hi, 6);
        Assert.True(hi <= 0.50, $"mid-slider window [{lo},{hi}] must stay below the median, or the default serves noise");
    }

    [Fact]
    public void Percentiles_EndpointsAreFixedPointsUnderTheCurve()
    {
        // The curve redistributes the travel; it must not cap the reach. Comfort still means the
        // nearest fifth and Abyss still means the farthest fifth.
        Assert.Equal((0.0, 0.20), RingResolver.Percentiles(0.0, 0.20));
        Assert.Equal((0.80, 1.0), RingResolver.Percentiles(1.0, 0.20));
    }

    [Fact]
    public void Percentiles_CurveOfOne_RestoresTheLinearMap()
    {
        // The escape hatch: setting the curve to 1.0 reproduces the pre-D68 behaviour exactly.
        // If this fails, the option is not the knob its documentation claims.
        (double lo, double hi) = RingResolver.Percentiles(0.5, 0.20, reachCurve: 1.0);

        Assert.Equal(0.40, lo, 6);
        Assert.Equal(0.60, hi, 6);
    }

    [Fact]
    public void Percentiles_AreMonotonicInComfort()
    {
        // Bending the travel must not fold it: every step right still reaches strictly farther.
        double previous = -1.0;

        foreach (double comfort in new[] { 0.0, 0.1, 0.25, 0.5, 0.75, 0.9, 1.0 })
        {
            (double lo, _) = RingResolver.Percentiles(comfort, 0.20);
            Assert.True(lo > previous, $"comfort={comfort}: lo {lo} must exceed the previous {previous}");
            previous = lo;
        }
    }

    [Fact]
    public void Percentiles_RejectsDegenerateCurve()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RingResolver.Percentiles(0.5, 0.20, reachCurve: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RingResolver.Percentiles(0.5, 0.20, reachCurve: -1.0));
    }
}
