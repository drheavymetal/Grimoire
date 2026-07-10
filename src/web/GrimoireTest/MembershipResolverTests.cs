using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class MembershipResolverTests
{
    private static readonly Guid Band = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Member = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void QueryingBand_Backward_ResolvesTargetAsMember()
    {
        ResolvedMembership? m = MembershipResolver.Resolve(
            "member of band", "backward",
            Band, "Darkthrone", "Darkthrone", ArtistKind.Group,
            Member, "Fenriz", "Fenriz", ArtistKind.Person,
            "1986", null, ["drums (drum set)", "original"]);

        Assert.NotNull(m);
        Assert.Equal(Member, m!.MemberMbid);
        Assert.Equal(Band, m.BandMbid);
        Assert.Equal(new DateOnly(1986, 1, 1), m.Begin);
        Assert.Null(m.End);
        // "original" is a qualifier, not an instrument.
        Assert.Equal(["drums (drum set)"], m.Instruments);
    }

    [Fact]
    public void QueryingPerson_Forward_ResolvesQueriedAsMember()
    {
        ResolvedMembership? m = MembershipResolver.Resolve(
            "member of band", "forward",
            Member, "Einar Selvik", "Selvik, Einar", ArtistKind.Person,
            Band, "Gorgoroth", "Gorgoroth", ArtistKind.Group,
            "2003", "2004", ["percussion"]);

        Assert.NotNull(m);
        Assert.Equal(Member, m!.MemberMbid);
        Assert.Equal("Einar Selvik", m.MemberName);
        Assert.Equal(Band, m.BandMbid);
        Assert.Equal("Gorgoroth", m.BandName);
        Assert.Equal(new DateOnly(2003, 1, 1), m.Begin);
        Assert.Equal(new DateOnly(2004, 1, 1), m.End);
    }

    [Fact]
    public void NonMembershipRelation_ReturnsNull()
    {
        ResolvedMembership? m = MembershipResolver.Resolve(
            "artist rename", "backward",
            Band, "Band", null, ArtistKind.Group,
            Member, "Other", null, ArtistKind.Group,
            null, null, null);

        Assert.Null(m);
    }

    [Fact]
    public void GuestAttribute_IsNotAnOfficialMember()
    {
        ResolvedMembership? m = MembershipResolver.Resolve(
            "member of band", "backward",
            Band, "Band", null, ArtistKind.Group,
            Member, "Session Player", null, ArtistKind.Person,
            "1990", "1990", ["guitar", "guest"]);

        Assert.Null(m);
    }

    [Fact]
    public void SelfReferentialEdge_ReturnsNull()
    {
        ResolvedMembership? m = MembershipResolver.Resolve(
            "member of band", "backward",
            Band, "Band", null, ArtistKind.Group,
            Band, "Band", null, ArtistKind.Group,
            null, null, null);

        Assert.Null(m);
    }

    [Fact]
    public void Merge_TakesEarliestBegin_And_OpenEndWins()
    {
        ResolvedMembership first = new(Member, "M", null, ArtistKind.Person, Band, "B",
            new DateOnly(1990, 1, 1), new DateOnly(1995, 1, 1), ["guitar"]);
        ResolvedMembership second = new(Member, "M", null, ArtistKind.Person, Band, "B",
            new DateOnly(1988, 1, 1), null, ["bass"]);

        ResolvedMembership merged = MembershipResolver.Merge(first, second);

        Assert.Equal(new DateOnly(1988, 1, 1), merged.Begin);
        Assert.Null(merged.End); // an open stint means the member is still active
        Assert.Contains("guitar", merged.Instruments);
        Assert.Contains("bass", merged.Instruments);
    }

    [Fact]
    public void Merge_TwoClosedStints_TakesLatestEnd()
    {
        ResolvedMembership first = new(Member, "M", null, ArtistKind.Person, Band, "B",
            new DateOnly(1990, 1, 1), new DateOnly(1992, 1, 1), []);
        ResolvedMembership second = new(Member, "M", null, ArtistKind.Person, Band, "B",
            new DateOnly(1995, 1, 1), new DateOnly(1998, 1, 1), []);

        ResolvedMembership merged = MembershipResolver.Merge(first, second);

        Assert.Equal(new DateOnly(1990, 1, 1), merged.Begin);
        Assert.Equal(new DateOnly(1998, 1, 1), merged.End);
    }

    [Theory]
    [InlineData("1991", 1991, 1, 1)]
    [InlineData("1991-03", 1991, 3, 1)]
    [InlineData("1991-03-15", 1991, 3, 15)]
    [InlineData("1991-02-30", 1991, 2, 1)] // impossible day falls back to first of month
    public void ParseDate_HandlesPartialDates(string input, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), MembershipResolver.ParseDate(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void ParseDate_ReturnsNullForJunk(string? input)
    {
        Assert.Null(MembershipResolver.ParseDate(input));
    }
}
