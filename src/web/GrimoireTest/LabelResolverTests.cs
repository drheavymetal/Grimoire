using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class LabelResolverTests
{
    private const string Peaceville = "8d4c9b9a-05cb-4eec-a354-62638559b717";

    [Fact]
    public void Resolve_ValidLabel_ProducesRecord()
    {
        ResolvedLabel? label = LabelResolver.Resolve(Peaceville, "Peaceville", "GB");

        Assert.NotNull(label);
        Assert.Equal(Guid.Parse(Peaceville), label!.Mbid);
        Assert.Equal("Peaceville", label.Name);
        Assert.Equal("GB", label.Country);
    }

    [Fact]
    public void Resolve_NoCountry_LeavesCountryNull()
    {
        ResolvedLabel? label = LabelResolver.Resolve(Peaceville, "Peaceville", null);

        Assert.NotNull(label);
        Assert.Null(label!.Country);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Resolve_BadMbid_ReturnsNull(string? mbid)
    {
        Assert.Null(LabelResolver.Resolve(mbid, "Peaceville", "GB"));
    }

    [Fact]
    public void Resolve_BlankName_ReturnsNull()
    {
        Assert.Null(LabelResolver.Resolve(Peaceville, "  ", "GB"));
    }

    [Fact]
    public void First_PicksFirstValid_SkippingBlanks()
    {
        (string?, string?, string?)[] infos =
        [
            (null, "No id", null),
            (Peaceville, "Peaceville", null),
            ("11111111-1111-1111-1111-111111111111", "Later", null),
        ];

        ResolvedLabel? label = LabelResolver.First(infos);

        Assert.NotNull(label);
        Assert.Equal("Peaceville", label!.Name);
    }

    [Fact]
    public void First_NoneValid_ReturnsNull()
    {
        (string?, string?, string?)[] infos = [(null, "x", null), ("bad", "y", null)];
        Assert.Null(LabelResolver.First(infos));
    }
}
