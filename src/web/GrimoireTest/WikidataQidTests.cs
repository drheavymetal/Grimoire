using Grimoire.Library.Wikidata;
using Xunit;

namespace Grimoire.Tests;

public class WikidataQidTests
{
    [Fact]
    public void FromUri_WikiPageUrl_ExtractsQid()
    {
        // The form stored in artists.links['wikidata'].
        Assert.Equal("Q220938", WikidataQid.FromUri("https://www.wikidata.org/wiki/Q220938"));
    }

    [Fact]
    public void FromUri_EntityUri_ExtractsQid()
    {
        // The form SPARQL returns in a binding value.
        Assert.Equal("Q487479", WikidataQid.FromUri("http://www.wikidata.org/entity/Q487479"));
    }

    [Fact]
    public void FromUri_StripsFragmentAndQuery()
    {
        Assert.Equal("Q16005", WikidataQid.FromUri("https://www.wikidata.org/wiki/Q16005#sitelinks"));
        Assert.Equal("Q16005", WikidataQid.FromUri("https://www.wikidata.org/wiki/Q16005?uselang=en"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://www.wikidata.org/wiki/Property:P737")]
    [InlineData("https://en.wikipedia.org/wiki/Darkthrone")]
    [InlineData("https://www.wikidata.org/wiki/Lexeme:L1")]
    [InlineData("Q")]
    [InlineData("Q0")]
    public void FromUri_NonQid_ReturnsNull(string? uri)
    {
        Assert.Null(WikidataQid.FromUri(uri));
    }

    [Fact]
    public void ToPrefixed_AddsWdPrefix()
    {
        Assert.Equal("wd:Q220938", WikidataQid.ToPrefixed("Q220938"));
    }
}
