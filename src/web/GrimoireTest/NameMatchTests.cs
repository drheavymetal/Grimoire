using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class NameMatchTests
{
    [Theory]
    [InlineData("SKÁLD", "Skáld")]
    [InlineData("Skáld", "skald")]
    [InlineData("Darkthrone", "DARKTHRONE")]
    [InlineData("Old Man's Child", "old man's  child")]
    public void EquivalentNames_Match(string a, string b)
    {
        Assert.True(NameMatch.Matches(a, b));
    }

    [Theory]
    [InlineData("Death", "Toto")]
    [InlineData("Darkthrone", "Darkthrone Tribute")]
    [InlineData("Mayhem", "Mayhemic")]
    public void DifferentNames_DoNotMatch(string a, string b)
    {
        Assert.False(NameMatch.Matches(a, b));
    }

    [Fact]
    public void Diacritics_AreStripped()
    {
        Assert.Equal("skald", NameMatch.Normalize("SKÁLD"));
    }

    [Fact]
    public void Whitespace_IsCollapsed()
    {
        Assert.Equal("old mans child", NameMatch.Normalize("  Old  Mans   Child "));
    }
}
