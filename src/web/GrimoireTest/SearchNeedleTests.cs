using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The needle guard behind the Rite's tag/theme lanes and the browse door: trim, lower-case, drop
/// empties, cap length. A pathological or blank needle must never silently become "match everything".
/// </summary>
public class SearchNeedleTests
{
    [Fact]
    public void Clean_TrimsAndLowerCases()
    {
        Assert.Equal("black metal", SearchNeedle.Clean("  Black Metal  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Clean_BlankIsNull(string? raw)
    {
        Assert.Null(SearchNeedle.Clean(raw));
    }

    [Fact]
    public void Clean_CapsAtMaxLength()
    {
        string overlong = new('a', SearchNeedle.MaxLength + 50);

        Assert.Equal(SearchNeedle.MaxLength, SearchNeedle.Clean(overlong)!.Length);
    }

    [Fact]
    public void Clean_KeepsWithinLimitIntact()
    {
        Assert.Equal("doom", SearchNeedle.Clean("doom"));
    }
}
