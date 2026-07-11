using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The taste trajectory drift (feature C16). These bite on the per-step and total drift the view
/// draws: a moving taste must show non-zero drift, a still one zero, and one snapshot no drift at all.
/// </summary>
public class TrajectoryMathTests
{
    [Fact]
    public void DriftSeries_IsPairwiseCosineDistance()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [0f, 1f, 0f]; // orthogonal to a → cosine distance 1
        float[] c = [0f, 1f, 0f]; // identical to b → cosine distance 0

        double[] drift = TrajectoryMath.DriftSeries([a, b, c]);

        Assert.Equal(2, drift.Length);
        Assert.Equal(1.0, drift[0], 6); // a → b
        Assert.Equal(0.0, drift[1], 6); // b → c (no movement)
    }

    [Fact]
    public void DriftSeries_SingleSnapshot_HasNoDrift()
    {
        Assert.Empty(TrajectoryMath.DriftSeries([[1f, 2f, 3f]]));
        Assert.Empty(TrajectoryMath.DriftSeries([]));
    }

    [Fact]
    public void TotalDrift_IsFirstToLast_NotSumOfSteps()
    {
        float[] a = [1f, 0f];
        float[] b = [0f, 1f];
        float[] c = [1f, 0f]; // wandered out to b and back to a

        // Sum of steps would be 2 (1 + 1); total drift (first→last) is 0 — they are the same point.
        Assert.Equal(0.0, TrajectoryMath.TotalDrift([a, b, c]), 6);

        double[] steps = TrajectoryMath.DriftSeries([a, b, c]);
        Assert.Equal(2.0, steps.Sum(), 6);
    }

    [Fact]
    public void TotalDrift_FewerThanTwo_IsZero()
    {
        Assert.Equal(0.0, TrajectoryMath.TotalDrift([[1f, 0f]]));
        Assert.Equal(0.0, TrajectoryMath.TotalDrift([]));
    }
}
