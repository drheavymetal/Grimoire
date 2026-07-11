using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// Comparing two bands by their tags (B24). These bite: case-folding, de-duplication, the empty
/// set, and the Jaccard arithmetic.
/// </summary>
public class CompareMathTests
{
    [Fact]
    public void SharedTags_IsTheCaseInsensitiveIntersection()
    {
        string[] a = ["Death Metal", "doom", "sludge"];
        string[] b = ["death metal", "DOOM", "crust"];

        Assert.Equal(["death metal", "doom"], CompareMath.SharedTags(a, b));
    }

    [Fact]
    public void SharedTags_IsEmptyWhenNothingOverlaps()
    {
        Assert.Empty(CompareMath.SharedTags(["a", "b"], ["c", "d"]));
    }

    [Fact]
    public void TagJaccard_IsIntersectionOverUnion()
    {
        // {a,b,c} vs {b,c,d}: intersection 2, union 4 → 0.5.
        Assert.Equal(0.5, CompareMath.TagJaccard(["a", "b", "c"], ["b", "c", "d"]), 6);
    }

    [Fact]
    public void TagJaccard_IsOneForIdenticalSets()
    {
        Assert.Equal(1.0, CompareMath.TagJaccard(["a", "b"], ["B", "a"]), 6);
    }

    [Fact]
    public void TagJaccard_IsZeroWhenBothAreEmpty()
    {
        // No evidence of similarity, and no divide-by-zero (flip the guard and this throws).
        Assert.Equal(0.0, CompareMath.TagJaccard([], []), 6);
    }

    [Fact]
    public void TagJaccard_IsZeroWhenDisjoint()
    {
        Assert.Equal(0.0, CompareMath.TagJaccard(["a"], ["b"]), 6);
    }
}
