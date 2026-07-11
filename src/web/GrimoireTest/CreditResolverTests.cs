using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class CreditResolverTests
{
    private static readonly Guid Fenriz = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Recording = Guid.Parse("749b7f1d-d58a-4f0f-85ef-311479d504c9");
    private static readonly HashSet<Guid> Corpus = [Fenriz];

    [Fact]
    public void Instrument_MapsToPerformer_WithInstrument()
    {
        IReadOnlyList<CreditFacet> facets = CreditResolver.Facets("instrument", ["drums (drum set)"]);

        CreditFacet facet = Assert.Single(facets);
        Assert.Equal(CreditResolver.RolePerformer, facet.Role);
        Assert.Equal("drums (drum set)", facet.Instrument);
        Assert.False(facet.IsGuest);
    }

    [Fact]
    public void Instrument_MultipleInstruments_YieldOneFacetEach()
    {
        IReadOnlyList<CreditFacet> facets = CreditResolver.Facets("instrument", ["guitar", "bass guitar"]);

        Assert.Equal(2, facets.Count);
        Assert.Contains(facets, f => f.Instrument == "guitar");
        Assert.Contains(facets, f => f.Instrument == "bass guitar");
        Assert.All(facets, f => Assert.Equal(CreditResolver.RolePerformer, f.Role));
    }

    [Fact]
    public void Instrument_QualifierAttributes_AreNotInstruments()
    {
        // "original" and "solo" qualify the performance; only "guitar" is the instrument.
        IReadOnlyList<CreditFacet> facets = CreditResolver.Facets("instrument", ["guitar", "original", "solo"]);

        CreditFacet facet = Assert.Single(facets);
        Assert.Equal("guitar", facet.Instrument);
    }

    [Fact]
    public void Vocal_WithDescriptor_UsesIt()
    {
        CreditFacet facet = Assert.Single(CreditResolver.Facets("vocal", ["choir vocals"]));
        Assert.Equal(CreditResolver.RolePerformer, facet.Role);
        Assert.Equal("choir vocals", facet.Instrument);
    }

    [Fact]
    public void Vocal_WithoutDescriptor_FallsBackToVocals()
    {
        CreditFacet facet = Assert.Single(CreditResolver.Facets("vocal", null));
        Assert.Equal(CreditResolver.Vocals, facet.Instrument);
    }

    [Theory]
    [InlineData("producer", "producer")]
    [InlineData("engineer", "engineer")]
    [InlineData("mix", "mix")]
    [InlineData("mastering", "master")]
    public void ProductionRelations_MapToTheirRole_WithNoInstrument(string type, string role)
    {
        CreditFacet facet = Assert.Single(CreditResolver.Facets(type, null));
        Assert.Equal(role, facet.Role);
        Assert.Null(facet.Instrument);
    }

    [Fact]
    public void GuestAttribute_FlagsGuest_ButKeepsInstrument()
    {
        CreditFacet facet = Assert.Single(CreditResolver.Facets("instrument", ["keyboard", "guest"]));
        Assert.True(facet.IsGuest);
        Assert.Equal("keyboard", facet.Instrument);
    }

    [Fact]
    public void UnknownRelationType_YieldsNothing()
    {
        Assert.Empty(CreditResolver.Facets("composer", null));
        Assert.Empty(CreditResolver.Facets(null, null));
        Assert.Empty(CreditResolver.Facets("", null));
    }

    [Fact]
    public void Resolve_KeepsCorpusArtist_AndCarriesRecording()
    {
        IReadOnlyList<ResolvedCredit> credits =
            CreditResolver.Resolve("instrument", ["drums (drum set)"], Fenriz, Recording, Corpus);

        ResolvedCredit credit = Assert.Single(credits);
        Assert.Equal(Fenriz, credit.ArtistMbid);
        Assert.Equal(Recording, credit.RecordingMbid);
        Assert.Equal(CreditResolver.RolePerformer, credit.Role);
        Assert.Equal("drums (drum set)", credit.Instrument);
    }

    [Fact]
    public void Resolve_DropsArtistOutsideCorpus()
    {
        Guid stranger = Guid.Parse("99999999-9999-9999-9999-999999999999");

        IReadOnlyList<ResolvedCredit> credits =
            CreditResolver.Resolve("instrument", ["guitar"], stranger, Recording, Corpus);

        Assert.Empty(credits);
    }

    [Fact]
    public void Resolve_DropsEmptyArtistMbid()
    {
        Assert.Empty(CreditResolver.Resolve("instrument", ["guitar"], Guid.Empty, Recording, Corpus));
    }
}
