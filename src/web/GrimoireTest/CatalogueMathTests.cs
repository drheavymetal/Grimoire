using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The catalogue curiosities (C24 one-album, C25 hyperprolific). These bite on the boundaries:
/// exactly one album, one more release than years alive, and the formed-this-year divide-by-zero.
/// </summary>
public class CatalogueMathTests
{
    [Theory]
    [InlineData(1, 0, 0, true)]  // exactly one album, nothing else: the classic one-and-done.
    [InlineData(1, 1, 0, false)] // an album plus an EP is not one-album.
    [InlineData(1, 0, 1, false)] // an album plus a demo is not one-album.
    [InlineData(2, 0, 0, false)] // two albums.
    [InlineData(0, 1, 0, false)] // an EP alone is not an album.
    [InlineData(0, 0, 0, false)] // nothing.
    public void IsOneAlbumBand_MatchesTheStrictDefinition(int albums, int eps, int demos, bool expected)
    {
        Assert.Equal(expected, CatalogueMath.IsOneAlbumBand(albums, eps, demos));
    }

    [Fact]
    public void ProlificacyRatio_IsReleasesPerYearAlive()
    {
        // 16 releases, formed 2010, now 2026 → 16 years → exactly 1.0.
        Assert.Equal(1.0, CatalogueMath.ProlificacyRatio(16, 2010, 2026), 6);
        // 23 releases in 23 years → 1.0.
        Assert.Equal(1.0, CatalogueMath.ProlificacyRatio(23, 2003, 2026), 6);
    }

    [Fact]
    public void ProlificacyRatio_FloorsYearsAtOneToAvoidDivideByZero()
    {
        // Formed this year: 3 releases over max(1, 0) = 1 year → 3.0, never infinity.
        Assert.Equal(3.0, CatalogueMath.ProlificacyRatio(3, 2026, 2026), 6);
    }

    [Theory]
    [InlineData(25, 2016, 2026, true)]  // 25 releases in 10 years → 2.5 > 1.
    [InlineData(10, 2016, 2026, false)] // 10 releases in 10 years → exactly 1, not strictly above.
    [InlineData(9, 2016, 2026, false)]  // fewer releases than years.
    public void IsHyperprolific_RequiresStrictlyMoreReleasesThanYears(int releases, int formed, int now, bool expected)
    {
        Assert.Equal(expected, CatalogueMath.IsHyperprolific(releases, formed, now));
    }
}
