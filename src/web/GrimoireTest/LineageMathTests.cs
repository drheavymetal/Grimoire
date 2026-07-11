using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The two non-graph lineage helpers: the diaspora "went after leaving" test (B11) and the
/// missing-link midpoint (C5). These bite — flip an inequality or an average and a case fails.
/// </summary>
public class LineageMathTests
{
    [Fact]
    public void WentAfterLeaving_TrueWhenJoinedAfterLeaving()
    {
        DateOnly left = new(1994, 1, 1);
        DateOnly joined = new(1995, 6, 1);

        Assert.True(LineageMath.WentAfterLeaving(left, joined));
    }

    [Fact]
    public void WentAfterLeaving_TrueOnTheSameDay()
    {
        DateOnly day = new(1994, 1, 1);

        // Inclusive: joining the day you left still counts as "went after". (Swap >= for > and this fails.)
        Assert.True(LineageMath.WentAfterLeaving(day, day));
    }

    [Fact]
    public void WentAfterLeaving_FalseWhenJoinedBeforeLeaving()
    {
        Assert.False(LineageMath.WentAfterLeaving(new DateOnly(1994, 1, 1), new DateOnly(1990, 1, 1)));
    }

    [Fact]
    public void WentAfterLeaving_FalseWhenEitherDateIsUnknown()
    {
        // No order is invented from a missing date (R2).
        Assert.False(LineageMath.WentAfterLeaving(null, new DateOnly(1995, 1, 1)));
        Assert.False(LineageMath.WentAfterLeaving(new DateOnly(1994, 1, 1), null));
        Assert.False(LineageMath.WentAfterLeaving(null, null));
    }

    [Fact]
    public void Midpoint_IsTheElementwiseAverage()
    {
        float[] a = [0f, 2f, 4f];
        float[] b = [2f, 2f, 0f];

        Assert.Equal(new[] { 1f, 2f, 2f }, LineageMath.Midpoint(a, b));
    }

    [Fact]
    public void Midpoint_DoesNotMutateInputs()
    {
        float[] a = [1f, 1f];
        float[] b = [3f, 3f];

        LineageMath.Midpoint(a, b);

        Assert.Equal(new[] { 1f, 1f }, a);
        Assert.Equal(new[] { 3f, 3f }, b);
    }

    [Fact]
    public void Midpoint_RejectsMismatchedDimensions()
    {
        Assert.Throws<ArgumentException>(() => LineageMath.Midpoint([1f, 2f], [1f]));
    }

    [Fact]
    public void Midpoint_RejectsEmptyVectors()
    {
        Assert.Throws<ArgumentException>(() => LineageMath.Midpoint([], []));
    }
}
