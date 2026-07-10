using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class VectorMathTests
{
    [Fact]
    public void Mean_IsElementwiseAverage()
    {
        float[] mean = VectorMath.Mean([[1f, 2f, 3f], [3f, 4f, 5f]]);

        Assert.Equal(2f, mean[0], 5);
        Assert.Equal(3f, mean[1], 5);
        Assert.Equal(4f, mean[2], 5);
    }

    [Fact]
    public void Mean_EmptySet_Throws()
    {
        Assert.Throws<ArgumentException>(() => VectorMath.Mean([]));
    }

    [Fact]
    public void Mean_MismatchedDimensions_Throws()
    {
        Assert.Throws<ArgumentException>(() => VectorMath.Mean([[1f, 2f], [1f]]));
    }

    [Fact]
    public void Subtract_RemovesTheMean()
    {
        float[] result = VectorMath.Subtract([5f, 5f], [2f, 3f]);

        Assert.Equal(3f, result[0], 5);
        Assert.Equal(2f, result[1], 5);
    }

    [Fact]
    public void CosineDistance_IdenticalDirection_IsZero()
    {
        Assert.Equal(0.0, VectorMath.CosineDistance([1f, 2f, 3f], [2f, 4f, 6f]), 6);
    }

    [Fact]
    public void CosineDistance_Orthogonal_IsOne()
    {
        Assert.Equal(1.0, VectorMath.CosineDistance([1f, 0f], [0f, 1f]), 6);
    }

    [Fact]
    public void CosineDistance_Opposite_IsTwo()
    {
        Assert.Equal(2.0, VectorMath.CosineDistance([1f, 0f], [-1f, 0f]), 6);
    }

    [Fact]
    public void CosineDistance_ZeroVector_IsOne_NotNaN()
    {
        double d = VectorMath.CosineDistance([0f, 0f], [1f, 1f]);

        Assert.False(double.IsNaN(d));
        Assert.Equal(1.0, d, 6);
    }

    [Fact]
    public void Centering_ExpandsSeparation()
    {
        // Three near-parallel vectors sit in a thin cone: their pairwise cosine distances are
        // tiny. Subtracting the mean pushes them apart — the mechanism behind D26.
        float[] a = [10f, 1f, 0f];
        float[] b = [10f, 0f, 1f];
        float[] c = [10f, 1f, 1f];

        double before = VectorMath.CosineDistance(a, b);

        float[] mean = VectorMath.Mean([a, b, c]);
        double after = VectorMath.CosineDistance(VectorMath.Subtract(a, mean), VectorMath.Subtract(b, mean));

        Assert.True(after > before * 2,
            $"centering should widen separation: before={before:F4}, after={after:F4}");
    }
}
