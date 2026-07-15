using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class RiteGenresTests
{
    [Fact]
    public void NeedleFor_KnownKey_ReturnsLowercaseSubstring()
    {
        Assert.Equal("black metal", RiteGenres.NeedleFor("black-metal"));
        Assert.Equal("viking", RiteGenres.NeedleFor("viking-metal"));
    }

    [Fact]
    public void NeedleFor_IsCaseInsensitiveOnTheKey()
    {
        Assert.Equal("thrash", RiteGenres.NeedleFor("Thrash-Metal"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-genre")]
    public void NeedleFor_UnknownOrBlank_ReturnsNull_ForAFullyOpenRite(string? key)
    {
        Assert.Null(RiteGenres.NeedleFor(key));
    }

    [Fact]
    public void All_KeysAreUnique_AndNeedlesNonEmpty()
    {
        var keys = new HashSet<string>();
        foreach (RiteGenre g in RiteGenres.All)
        {
            Assert.True(keys.Add(g.Key), $"duplicate key {g.Key}");
            Assert.False(string.IsNullOrWhiteSpace(g.Needle));
            Assert.False(string.IsNullOrWhiteSpace(g.Label));
        }
    }
}
