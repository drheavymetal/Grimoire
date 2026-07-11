using Grimoire.Library.Wikidata;
using Xunit;

namespace Grimoire.Tests;

public class WikidataDeathsTests
{
    // --- ParseDate ---

    [Fact]
    public void ParseDate_IsoTimestamp_ReturnsDate()
    {
        Assert.Equal(new DateOnly(1993, 8, 8), WikidataDeaths.ParseDate("1993-08-08T00:00:00Z"));
    }

    [Fact]
    public void ParseDate_YearOnlyPrecision_ComesThroughAsJanuaryFirst()
    {
        // Reduced-precision Wikidata dates arrive as YYYY-01-01T...
        Assert.Equal(new DateOnly(2004, 1, 1), WikidataDeaths.ParseDate("2004-01-01T00:00:00Z"));
    }

    [Fact]
    public void ParseDate_PlainDate_ReturnsDate()
    {
        Assert.Equal(new DateOnly(2001, 4, 8), WikidataDeaths.ParseDate("2001-04-08"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("-0044-03-15T00:00:00Z")] // BCE: dropped, does not apply to modern musicians
    public void ParseDate_UnparseableOrBce_ReturnsNull(string? value)
    {
        Assert.Null(WikidataDeaths.ParseDate(value));
    }

    // --- Parse ---

    [Fact]
    public void Parse_ReadsQidDateAndPlace()
    {
        SparqlResults results = new();
        results.Bindings.Add(new Dictionary<string, SparqlValue>
        {
            ["a"] = new SparqlValue { Value = "http://www.wikidata.org/entity/Q504330" },
            ["death"] = new SparqlValue { Value = "1993-08-08T00:00:00Z" },
            ["placeLabel"] = new SparqlValue { Value = "Bergen" },
        });

        WikidataDeaths.Death death = Assert.Single(WikidataDeaths.Parse(new SparqlResponse { Results = results }));

        Assert.Equal("Q504330", death.Qid);
        Assert.Equal(new DateOnly(1993, 8, 8), death.Date);
        Assert.Equal("Bergen", death.Place);
    }

    [Fact]
    public void Parse_PlaceOptional_NullWhenAbsent()
    {
        SparqlResults results = new();
        results.Bindings.Add(new Dictionary<string, SparqlValue>
        {
            ["a"] = new SparqlValue { Value = "http://www.wikidata.org/entity/Q1" },
            ["death"] = new SparqlValue { Value = "1980-12-08T00:00:00Z" },
            // no placeLabel
        });

        WikidataDeaths.Death death = Assert.Single(WikidataDeaths.Parse(new SparqlResponse { Results = results }));

        Assert.Equal(new DateOnly(1980, 12, 8), death.Date);
        Assert.Null(death.Place);
    }

    [Fact]
    public void Parse_SkipsRowWithoutQid()
    {
        SparqlResults results = new();
        results.Bindings.Add(new Dictionary<string, SparqlValue>
        {
            ["death"] = new SparqlValue { Value = "1993-08-08T00:00:00Z" },
        });

        Assert.Empty(WikidataDeaths.Parse(new SparqlResponse { Results = results }));
    }

    [Fact]
    public void Parse_NullResponse_ReturnsEmpty()
    {
        Assert.Empty(WikidataDeaths.Parse(null));
    }
}
