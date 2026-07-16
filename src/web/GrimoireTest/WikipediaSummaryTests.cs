using System.Text.Json;
using Grimoire.Library.Services;
using Grimoire.Library.Wikidata;
using Xunit;

namespace Grimoire.Tests;

public class WikipediaSummaryTests
{
    private static SparqlResponse? Parse(string json) =>
        JsonSerializer.Deserialize<SparqlResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

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

    // --- SiteUrl / LanguageOf ---

    [Theory]
    [InlineData("en", "https://en.wikipedia.org/")]
    [InlineData("es", "https://es.wikipedia.org/")]
    [InlineData("no", "https://no.wikipedia.org/")]
    public void SiteUrl_IsTheEditionsOwnHost(string language, string expected)
    {
        Assert.Equal(expected, WikipediaSummary.SiteUrl(language));
    }

    [Fact]
    public void SiteUrl_AndLanguageOf_AreInverses()
    {
        // The property the whole design leans on: because schema:isPartOf IS the article's host, an
        // article can be routed back to its edition with nothing kept in sync on our side. If these
        // ever drift apart, every non-English biography silently lands under the wrong language.
        foreach (string language in new[] { "en", "es", "no", "sv", "fi", "de" })
        {
            string article = $"{WikipediaSummary.SiteUrl(language)}wiki/Darkthrone";

            Assert.Equal(language, WikipediaSummary.LanguageOf(article));
        }
    }

    [Theory]
    [InlineData("https://es.wikipedia.org/wiki/Héroes_del_Silencio", "es")]
    [InlineData("https://no.wikipedia.org/wiki/Darkthrone", "no")]
    [InlineData("https://EN.WIKIPEDIA.ORG/wiki/Darkthrone", "en")]
    public void LanguageOf_ReadsTheEditionOffTheHost(string url, string expected)
    {
        Assert.Equal(expected, WikipediaSummary.LanguageOf(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://example.org/wiki/Darkthrone")]
    [InlineData("https://wikipedia.org/wiki/Darkthrone")]
    // The mobile host must not read as the language "en.m" — nor be mistaken for "en".
    [InlineData("https://en.m.wikipedia.org/wiki/Darkthrone")]
    [InlineData("https://www.wikidata.org/wiki/Q131144")]
    public void LanguageOf_RejectsWhatIsNotAnArticleAddress(string? url)
    {
        Assert.Null(WikipediaSummary.LanguageOf(url));
    }

    // --- ParseArticleTitles (batched, multi-language) ---

    // What WDQS actually returns for a two-language batch: the same artist appears once per edition,
    // rows for different artists and languages interleaved, in no promised order.
    private const string BatchSparqlJson = """
    {
      "head": { "vars": ["mbid", "article"] },
      "results": {
        "bindings": [
          {
            "mbid": { "type": "literal", "value": "aaaaaaaa-0000-0000-0000-000000000001" },
            "article": { "type": "uri", "value": "https://en.wikipedia.org/wiki/Darkthrone" }
          },
          {
            "mbid": { "type": "literal", "value": "bbbbbbbb-0000-0000-0000-000000000002" },
            "article": { "type": "uri", "value": "https://es.wikipedia.org/wiki/Héroes_del_Silencio" }
          },
          {
            "mbid": { "type": "literal", "value": "aaaaaaaa-0000-0000-0000-000000000001" },
            "article": { "type": "uri", "value": "https://es.wikipedia.org/wiki/Darkthrone" }
          }
        ]
      }
    }
    """;

    [Fact]
    public void ParseArticleTitles_MapsEachRowToItsArtistAndItsLanguage()
    {
        Dictionary<string, Dictionary<string, string>> titles =
            WikipediaSummary.ParseArticleTitles(Parse(BatchSparqlJson));

        Assert.Equal(2, titles.Count);

        // One artist, two editions — the rows arrived apart and must still land together.
        Dictionary<string, string> darkthrone = titles["aaaaaaaa-0000-0000-0000-000000000001"];
        Assert.Equal(2, darkthrone.Count);
        Assert.Equal("Darkthrone", darkthrone["en"]);
        Assert.Equal("Darkthrone", darkthrone["es"]);
    }

    [Fact]
    public void ParseArticleTitles_KeepsASpanishOnlyArtist()
    {
        // The case that motivates the whole feature: a band eswiki covers and enwiki does not.
        Dictionary<string, Dictionary<string, string>> titles =
            WikipediaSummary.ParseArticleTitles(Parse(BatchSparqlJson));

        Dictionary<string, string> heroes = titles["bbbbbbbb-0000-0000-0000-000000000002"];

        Assert.Equal("Héroes_del_Silencio", heroes["es"]);
        Assert.False(heroes.ContainsKey("en"));
    }

    [Fact]
    public void ParseArticleTitles_LeavesNonAsciiTitlesRawForSummaryPathToEscapeOnce()
    {
        // WDQS returns IRIs with the characters literal, and SummaryPath escapes them exactly once.
        // If a title arrived percent-encoded and were escaped again, '%' would become '%25' and every
        // accented Spanish band would 404 — and be stamped as "no biography" for ever. Spanish is
        // full of accents, so this is the difference between a working pass and a silent one.
        Dictionary<string, Dictionary<string, string>> titles =
            WikipediaSummary.ParseArticleTitles(Parse(BatchSparqlJson));

        string title = titles["bbbbbbbb-0000-0000-0000-000000000002"]["es"];

        Assert.Equal("Héroes_del_Silencio", title);
        Assert.DoesNotContain('%', title);
        Assert.Equal("api/rest_v1/page/summary/H%C3%A9roes_del_Silencio", WikipediaSummary.SummaryPath(title));
    }

    [Fact]
    public void ParseArticleTitles_IsCaseInsensitiveOnMbidAndLanguage()
    {
        Dictionary<string, Dictionary<string, string>> titles =
            WikipediaSummary.ParseArticleTitles(Parse(BatchSparqlJson));

        // A GUID stringified upper-case must still find the binding (SPARQL echoes lower-case).
        Assert.Equal("Darkthrone", titles["AAAAAAAA-0000-0000-0000-000000000001"]["EN"]);
    }

    [Fact]
    public void ParseArticleTitles_NoBindings_ReturnsEmptyMap()
    {
        Assert.Empty(WikipediaSummary.ParseArticleTitles(Parse("""{ "results": { "bindings": [] } }""")));
        Assert.Empty(WikipediaSummary.ParseArticleTitles(null));
    }

    [Fact]
    public void ParseArticleTitles_SkipsIncompleteAndUnusableRows()
    {
        // Rows missing a variable, or carrying a host that is not an edition, must be dropped
        // WITHOUT taking the good rows in the same batch down with them.
        const string mixed = """
        {
          "results": {
            "bindings": [
              { "article": { "type": "uri", "value": "https://en.wikipedia.org/wiki/Orphan" } },
              { "mbid": { "type": "literal", "value": "cccccccc-0000-0000-0000-000000000003" } },
              {
                "mbid": { "type": "literal", "value": "eeeeeeee-0000-0000-0000-000000000005" },
                "article": { "type": "uri", "value": "https://example.org/wiki/Nope" }
              },
              {
                "mbid": { "type": "literal", "value": "ffffffff-0000-0000-0000-000000000006" },
                "article": { "type": "uri", "value": "https://es.wikipedia.org/wiki/" }
              },
              {
                "mbid": { "type": "literal", "value": "dddddddd-0000-0000-0000-000000000004" },
                "article": { "type": "uri", "value": "https://en.wikipedia.org/wiki/Emperor_(band)" }
              }
            ]
          }
        }
        """;

        Dictionary<string, Dictionary<string, string>> titles = WikipediaSummary.ParseArticleTitles(Parse(mixed));

        Assert.Single(titles);
        Assert.Equal("Emperor_(band)", titles["dddddddd-0000-0000-0000-000000000004"]["en"]);
    }

    [Fact]
    public void ParseArticleTitles_DuplicateLanguageForOneArtist_KeepsTheFirst()
    {
        const string duplicated = """
        {
          "results": {
            "bindings": [
              {
                "mbid": { "type": "literal", "value": "aaaaaaaa-0000-0000-0000-000000000001" },
                "article": { "type": "uri", "value": "https://es.wikipedia.org/wiki/First" }
              },
              {
                "mbid": { "type": "literal", "value": "aaaaaaaa-0000-0000-0000-000000000001" },
                "article": { "type": "uri", "value": "https://es.wikipedia.org/wiki/Second" }
              }
            ]
          }
        }
        """;

        Dictionary<string, Dictionary<string, string>> titles = WikipediaSummary.ParseArticleTitles(Parse(duplicated));

        Assert.Equal("First", titles["aaaaaaaa-0000-0000-0000-000000000001"]["es"]);
    }

    // --- ParseSummary ---

    [Fact]
    public void ParseSummary_PullsExtractAndCanonicalUrl()
    {
        WikipediaBiography? bio = WikipediaSummary.ParseSummary(SummaryJson, "Darkthrone", "en");

        Assert.NotNull(bio);
        Assert.Equal("Darkthrone are a Norwegian black metal band, formed in 1986.", bio!.Abstract);
        Assert.Equal("https://en.wikipedia.org/wiki/Darkthrone", bio.Url);
    }

    [Fact]
    public void ParseSummary_FallsBackToTitleWhenNoContentUrls()
    {
        const string noUrls = """{ "extract": "Some biography text." }""";

        WikipediaBiography? bio = WikipediaSummary.ParseSummary(noUrls, "Some_Band", "en");

        Assert.NotNull(bio);
        Assert.Equal("https://en.wikipedia.org/wiki/Some_Band", bio!.Url);
    }

    [Fact]
    public void ParseSummary_FallbackUrlUsesTheArticlesOwnEditionNotEnglish()
    {
        // Attribution must credit the text shown. A Spanish extract fathered onto an enwiki URL is a
        // link to an article that may not even exist — and a CC BY-SA violation, not a cosmetic slip.
        const string noUrls = """{ "extract": "Banda española de rock." }""";

        WikipediaBiography? bio = WikipediaSummary.ParseSummary(noUrls, "Héroes_del_Silencio", "es");

        Assert.Equal("https://es.wikipedia.org/wiki/Héroes_del_Silencio", bio!.Url);
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
        Assert.Null(WikipediaSummary.ParseSummary(json, "X", "en"));
    }

    [Fact]
    public void SummaryPath_EscapesTheTitleIntoOneSegment()
    {
        Assert.Equal("api/rest_v1/page/summary/Darkthrone", WikipediaSummary.SummaryPath("Darkthrone"));
    }

    /// <summary>
    /// Titles reach us as the path of a WDQS-returned URL, and MediaWiki encodes those
    /// inconsistently: a slash stays raw, accents stay raw, an ampersand arrives already escaped.
    /// Escaping an already-escaped title again turned "%26" into "%2526", Wikipedia answered 404,
    /// and — a 404 being a perfectly good "no such article" — the band was stamped as having no
    /// biography for ever, on the strength of a URL we corrupted ourselves. Verified live against
    /// the real endpoint: the %2526 form 404s, the %26 form is 200.
    /// </summary>
    [Theory]
    // Already-escaped, must survive unchanged rather than gain a layer.
    [InlineData("Bob_Marley_%26_The_Wailers", "api/rest_v1/page/summary/Bob_Marley_%26_The_Wailers")]
    [InlineData("Emerson%2C_Lake_%26_Palmer", "api/rest_v1/page/summary/Emerson%2C_Lake_%26_Palmer")]
    // Raw, must be escaped exactly once.
    [InlineData("AC/DC", "api/rest_v1/page/summary/AC%2FDC")]
    [InlineData("Héroes_del_Silencio", "api/rest_v1/page/summary/H%C3%A9roes_del_Silencio")]
    // A literal percent is written %25 by MediaWiki, so decoding is the exact inverse and round-trips.
    [InlineData("100%25_Fun", "api/rest_v1/page/summary/100%25_Fun")]
    public void SummaryPath_EncodesExactlyOnceWhateverEncodingTheTitleArrivedIn(string title, string expected)
    {
        Assert.Equal(expected, WikipediaSummary.SummaryPath(title));
    }

    [Fact]
    public void SummaryPath_IsIdempotentOverItsOwnOutput()
    {
        // The property behind the fix: normalising first means a title cannot accumulate layers of
        // escaping no matter how many times it has been through an encoder already.
        const string title = "Bob_Marley_%26_The_Wailers";

        string once = WikipediaSummary.SummaryPath(title);
        string twice = WikipediaSummary.SummaryPath(once["api/rest_v1/page/summary/".Length..]);

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// The five real catalogue artists whose Wikipedia titles carry a slash. Unescaped they became
    /// extra path segments, Wikipedia answered 400, and the pass retried them every run for ever
    /// (MEMORY §6f). A slash must survive as %2F.
    /// </summary>
    [Theory]
    [InlineData("Fliflet/Hamre", "api/rest_v1/page/summary/Fliflet%2FHamre")]
    [InlineData("The Yes/No People", "api/rest_v1/page/summary/The%20Yes%2FNo%20People")]
    [InlineData("Bourne/Davis/Kane", "api/rest_v1/page/summary/Bourne%2FDavis%2FKane")]
    [InlineData("DAF/DOS", "api/rest_v1/page/summary/DAF%2FDOS")]
    [InlineData("r.o.r/s", "api/rest_v1/page/summary/r.o.r%2Fs")]
    public void SummaryPath_SlashInTitle_DoesNotBecomeAPathSegment(string title, string expected)
    {
        string path = WikipediaSummary.SummaryPath(title);

        Assert.Equal(expected, path);

        // Four segments, always: the title never splits the path no matter what it contains.
        Assert.Equal(5, path.Split('/').Length);
    }
}
