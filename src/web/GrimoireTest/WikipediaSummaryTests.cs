using System.Text.Json;
using Grimoire.Library.Services;
using Grimoire.Library.Wikidata;
using Xunit;

namespace Grimoire.Tests;

public class WikipediaSummaryTests
{
    // A real Wikidata SPARQL result shape: one binding whose ?article is the enwiki article URL.
    private const string SparqlJson = """
    {
      "head": { "vars": ["article"] },
      "results": {
        "bindings": [
          { "article": { "type": "uri", "value": "https://en.wikipedia.org/wiki/Darkthrone" } }
        ]
      }
    }
    """;

    // The Wikipedia REST summary body shape (api/rest_v1/page/summary/{title}).
    private const string SummaryJson = """
    {
      "type": "standard",
      "title": "Darkthrone",
      "extract": "Darkthrone are a Norwegian black metal band, formed in 1986.",
      "content_urls": {
        "desktop": { "page": "https://en.wikipedia.org/wiki/Darkthrone" },
        "mobile": { "page": "https://en.m.wikipedia.org/wiki/Darkthrone" }
      }
    }
    """;

    // --- ParseArticleTitle ---

    [Fact]
    public void ParseArticleTitle_ExtractsTitleAfterWiki()
    {
        SparqlResponse? response = JsonSerializer.Deserialize<SparqlResponse>(
            SparqlJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("Darkthrone", WikipediaSummary.ParseArticleTitle(response));
    }

    [Fact]
    public void ParseArticleTitle_NoBindings_ReturnsNull()
    {
        SparqlResponse? empty = JsonSerializer.Deserialize<SparqlResponse>(
            """{ "results": { "bindings": [] } }""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Null(WikipediaSummary.ParseArticleTitle(empty));
        Assert.Null(WikipediaSummary.ParseArticleTitle(null));
    }

    // --- ParseSummary ---

    [Fact]
    public void ParseSummary_PullsExtractAndCanonicalUrl()
    {
        WikipediaBiography? bio = WikipediaSummary.ParseSummary(SummaryJson, "Darkthrone");

        Assert.NotNull(bio);
        Assert.Equal("Darkthrone are a Norwegian black metal band, formed in 1986.", bio!.Abstract);
        Assert.Equal("https://en.wikipedia.org/wiki/Darkthrone", bio.Url);
    }

    [Fact]
    public void ParseSummary_FallsBackToTitleWhenNoContentUrls()
    {
        const string noUrls = """{ "extract": "Some biography text." }""";

        WikipediaBiography? bio = WikipediaSummary.ParseSummary(noUrls, "Some_Band");

        Assert.NotNull(bio);
        Assert.Equal("https://en.wikipedia.org/wiki/Some_Band", bio!.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"title\":\"X\"}")]
    [InlineData("{\"extract\":\"\"}")]
    [InlineData("{\"extract\":\"   \"}")]
    public void ParseSummary_MissingOrEmptyExtract_ReturnsNull(string? json)
    {
        Assert.Null(WikipediaSummary.ParseSummary(json, "X"));
    }
}
