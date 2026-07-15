using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The friend-handle validator (FRIENDS wave): 3-30 chars of [a-z0-9_], case-insensitive, stored
/// lower-cased. These bite on the length bounds, the character class, and the normalisation that
/// makes "Mercyful_Fate" and "mercyful_fate" the same handle.
/// </summary>
public class HandleValidatorTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("mercyful_fate")]
    [InlineData("a_1")]
    [InlineData("0123456789")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // exactly 30
    public void Normalize_AcceptsWellFormed(string handle)
    {
        Assert.NotNull(HandleValidator.Normalize(handle));
        Assert.True(HandleValidator.IsValid(handle));
    }

    [Theory]
    [InlineData("ab")]                                // too short (2)
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]   // too long (31)
    [InlineData("has space")]
    [InlineData("no-dash")]
    [InlineData("dot.dot")]
    [InlineData("emoji_\U0001F480")]
    [InlineData("")]
    public void Normalize_RejectsMalformed(string handle)
    {
        Assert.Null(HandleValidator.Normalize(handle));
        Assert.False(HandleValidator.IsValid(handle));
    }

    [Fact]
    public void Normalize_Null_IsNull()
    {
        Assert.Null(HandleValidator.Normalize(null));
        Assert.False(HandleValidator.IsValid(null));
    }

    [Fact]
    public void Normalize_LowercasesAndTrims()
    {
        Assert.Equal("mercyful_fate", HandleValidator.Normalize("  Mercyful_Fate  "));
        Assert.Equal("darkthrone", HandleValidator.Normalize("DARKTHRONE"));
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        string? once = HandleValidator.Normalize("Bathory_1983");
        string? twice = HandleValidator.Normalize(once);

        Assert.Equal("bathory_1983", once);
        Assert.Equal(once, twice);
    }
}
