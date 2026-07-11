using Grimoire.Library.Models;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// Per-release credit grouping (feature B9). These bite on the two decisions that matter: the
/// member-vs-guest split (D9) and the performer-vs-production split.
/// </summary>
public class CreditGroupingTests
{
    private static readonly Guid Release = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CreditGrouping.CreditRow Row(
        Guid artist, string name, string role, string? instrument, bool guest)
    {
        return new CreditGrouping.CreditRow(Release, artist, name, null, role, instrument, guest);
    }

    [Fact]
    public void Group_CollectsInstrumentsPerPerformer_AndSplitsProduction()
    {
        Guid james = Guid.NewGuid();
        Guid bob = Guid.NewGuid();

        var rows = new[]
        {
            Row(james, "James", "performer", "electric guitar", false),
            Row(james, "James", "performer", "lead vocals", false), // second instrument, same person
            Row(bob, "Bob", "producer", null, false),
        };

        var result = CreditGrouping.Group(rows);

        var release = Assert.Single(result);
        var james_credit = Assert.Single(release.Performers);
        Assert.Equal("James", james_credit.Name);
        Assert.Equal(new[] { "electric guitar", "lead vocals" }, james_credit.Instruments);
        Assert.False(james_credit.IsGuest);

        var production = Assert.Single(release.Production);
        Assert.Equal("Bob", production.Name);
        Assert.Equal("producer", production.Role);
    }

    [Fact]
    public void Group_MarksGuest_OnlyWhenEveryPerformerRowIsGuest()
    {
        Guid guest = Guid.NewGuid();
        Guid member = Guid.NewGuid();
        Guid mixed = Guid.NewGuid();

        var rows = new[]
        {
            Row(guest, "Guest", "performer", "flute", true),
            Row(member, "Member", "performer", "guitar", false),
            // One official credit and one guest credit -> counts as a member, not a guest.
            Row(mixed, "Mixed", "performer", "guitar", false),
            Row(mixed, "Mixed", "performer", "backing vocals", true),
        };

        var release = Assert.Single(CreditGrouping.Group(rows));

        // Members are ordered before guests; then by name. So: Member, Mixed, then Guest.
        Assert.Equal(new[] { "Member", "Mixed", "Guest" }, release.Performers.Select(p => p.Name).ToArray());

        Assert.False(release.Performers.Single(p => p.Name == "Member").IsGuest);
        Assert.False(release.Performers.Single(p => p.Name == "Mixed").IsGuest);
        Assert.True(release.Performers.Single(p => p.Name == "Guest").IsGuest);
    }

    [Fact]
    public void Group_OrdersProductionByRole()
    {
        Guid a = Guid.NewGuid();
        var rows = new[]
        {
            Row(a, "A", "master", null, false),
            Row(a, "A", "mix", null, false),
            Row(a, "A", "producer", null, false),
            Row(a, "A", "engineer", null, false),
        };

        var release = Assert.Single(CreditGrouping.Group(rows));
        Assert.Equal(
            new[] { "producer", "engineer", "mix", "master" },
            release.Production.Select(p => p.Role).ToArray());
    }

    [Fact]
    public void Group_ReturnsNothing_ForNoRows()
    {
        Assert.Empty(CreditGrouping.Group(Array.Empty<CreditGrouping.CreditRow>()));
    }
}
