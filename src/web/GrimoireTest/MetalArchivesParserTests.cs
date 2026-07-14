using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class MetalArchivesParserTests
{
    // A real ajax-band-search body shape (aaData rows of [linkHtml, genre, country]).
    private const string SearchJson = """
    {
      "error": "",
      "iTotalRecords": 3,
      "aaData": [
        ["<a href=\"https://www.metal-archives.com/bands/Darkthrone/146\">Darkthrone</a> (<strong>a.k.a.</strong> Dark Throne) <!-- 11.9 -->", "Death Metal (early); Black Metal (mid)", "Norway"],
        ["<a href=\"https://www.metal-archives.com/bands/Darkthrone_US/12345\">Darkthrone</a>  <!-- 5.9 -->", "Thrash Metal", "United States"],
        ["<a href=\"https://www.metal-archives.com/bands/Voids_of_Nirvana/999\">Voids of Nirvana</a>  <!-- 5.9 -->", "Black Metal", "United Kingdom"]
      ]
    }
    """;

    // The band_stats dt/dd block as it appears on a band page.
    private const string BandHtml = """
    <div id="band_stats">
      <dl class="float_left">
        <dt>Country of origin:</dt><dd><a href="#">Norway</a></dd>
        <dt>Location:</dt><dd>Kolbotn</dd>
        <dt>Status:</dt><dd>Active</dd>
      </dl>
      <dl class="float_right">
        <dt>Formed in:</dt><dd>1987</dd>
        <dt>Genre:</dt><dd>Black Metal</dd>
        <dt>Themes:</dt><dd>Anti-religion, Satan, Occultism, Death</dd>
      </dl>
    </div>
    <dl><dt>Elsewhere:</dt><dd>should not leak</dd></dl>
    """;

    // --- ParseSearch ---

    [Fact]
    public void ParseSearch_ExtractsIdNameGenreCountry()
    {
        var candidates = MetalArchivesParser.ParseSearch(SearchJson);

        Assert.Equal(3, candidates.Count);
        Assert.Equal(146, candidates[0].Id);
        Assert.Equal("Darkthrone", candidates[0].Name);
        Assert.Equal("Norway", candidates[0].Country);
        Assert.Contains("Black Metal", candidates[0].Genre);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"error\":\"x\"}")]
    public void ParseSearch_BadInput_ReturnsEmpty(string? json)
    {
        Assert.Empty(MetalArchivesParser.ParseSearch(json));
    }

    // --- Match (name + country, ambiguity → null) ---

    [Fact]
    public void Match_SingleNameAndCountryHit_Resolves()
    {
        var candidates = MetalArchivesParser.ParseSearch(SearchJson);

        MetalArchivesCandidate? hit = MetalArchivesParser.Match(candidates, "Darkthrone", "Norway");

        Assert.NotNull(hit);
        Assert.Equal(146, hit!.Id);
    }

    [Fact]
    public void Match_TwoSameNameSameCountry_IsAmbiguous_ReturnsNull()
    {
        // Two "Darkthrone" bands both in Norway would be an ambiguity: better no match than a guess.
        var candidates = new[]
        {
            new MetalArchivesCandidate(146, "Darkthrone", "Black Metal", "Norway"),
            new MetalArchivesCandidate(200, "Darkthrone", "Thrash", "Norway"),
        };

        Assert.Null(MetalArchivesParser.Match(candidates, "Darkthrone", "Norway"));
    }

    [Fact]
    public void Match_CountryDisambiguatesSameName()
    {
        // Same name, different countries: the Norwegian one is picked, the US one dropped.
        var candidates = MetalArchivesParser.ParseSearch(SearchJson);

        MetalArchivesCandidate? hit = MetalArchivesParser.Match(candidates, "Darkthrone", "United States");

        Assert.NotNull(hit);
        Assert.Equal(12345, hit!.Id);
    }

    [Fact]
    public void Match_NoNameMatch_ReturnsNull()
    {
        var candidates = MetalArchivesParser.ParseSearch(SearchJson);

        Assert.Null(MetalArchivesParser.Match(candidates, "Emperor", "Norway"));
    }

    [Fact]
    public void Match_DiacriticInsensitive()
    {
        // Combining diacritics fold (the same NameMatch as previews/listeners, D25): "Mörk" == "Mork".
        var candidates = new[] { new MetalArchivesCandidate(1, "Mörk Gryning", "Black Metal", "Sweden") };

        Assert.NotNull(MetalArchivesParser.Match(candidates, "Mork Gryning", "Sweden"));
    }

    // --- ParseBand ---

    [Fact]
    public void ParseBand_ExtractsStatsAndThemes()
    {
        MetalArchivesBand? band = MetalArchivesParser.ParseBand(BandHtml, 146, "Darkthrone");

        Assert.NotNull(band);
        Assert.Equal("Norway", band!.Country);
        Assert.Equal("Active", band.Status);
        Assert.Equal(1987, band.YearFormed);
        Assert.Equal("Black Metal", band.Genre);
        Assert.Equal(["Anti-religion", "Satan", "Occultism", "Death"], band.Themes);
    }

    [Fact]
    public void ParseBand_DoesNotLeakDtDdOutsideStatsBlock()
    {
        // The "Elsewhere" dt/dd after the stats div must not be read as a field.
        MetalArchivesBand? band = MetalArchivesParser.ParseBand(BandHtml, 146, "Darkthrone");

        Assert.NotNull(band);
        Assert.NotEqual("should not leak", band!.Country);
    }

    [Fact]
    public void ParseBand_NoStatsBlock_ReturnsNull()
    {
        Assert.Null(MetalArchivesParser.ParseBand("<html><body>nothing</body></html>", 1, "X"));
        Assert.Null(MetalArchivesParser.ParseBand(null, 1, "X"));
    }

    // --- ParseThemes ---

    [Fact]
    public void ParseThemes_SplitsTrimsDeduplicates()
    {
        Assert.Equal(
            ["Death", "Nature", "War"],
            MetalArchivesParser.ParseThemes("Death, Nature ; War, death"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("N/A")]
    public void ParseThemes_EmptyOrNA_ReturnsEmpty(string? value)
    {
        Assert.Empty(MetalArchivesParser.ParseThemes(value));
    }
}
