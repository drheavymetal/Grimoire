using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// Parsing a split title (C9). These bite: a slash-joined title yields its band parts, a plain
/// title yields nothing, and whitespace is trimmed.
/// </summary>
public class SplitTitleTests
{
    [Fact]
    public void Parts_SplitsOnSlashAndTrims()
    {
        Assert.Equal(["Xasthur", "Leviathan"], SplitTitle.Parts("Xasthur / Leviathan"));
    }

    [Fact]
    public void Parts_HandlesThreeWayySplits()
    {
        Assert.Equal(["Agathocles", "Rot", "Masher"], SplitTitle.Parts("Agathocles / Rot / Masher"));
    }

    [Fact]
    public void Parts_IsEmptyForATitleWithNoSlash()
    {
        // A plain album title is not a split (flip the guard and this would surface phantom bands).
        Assert.Empty(SplitTitle.Parts("Transilvanian Hunger"));
    }

    [Fact]
    public void Parts_IsEmptyForNullOrBlank()
    {
        Assert.Empty(SplitTitle.Parts(null));
        Assert.Empty(SplitTitle.Parts("   "));
    }
}
