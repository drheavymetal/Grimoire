using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Xunit;

namespace Grimoire.Tests;

public class LineupIntervalResolverTests
{
    private static ArtistEdge Member(string name, DateOnly? begin, DateOnly? end)
    {
        return new ArtistEdge
        {
            Id = Guid.NewGuid(),
            FromId = Guid.NewGuid(),
            ToId = Guid.NewGuid(),
            Kind = EdgeKind.MemberOf,
            BeginDate = begin,
            EndDate = end,
            Instruments = [name],
        };
    }

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void OpenEndedMember_IsActiveLongAfterJoining()
    {
        List<ArtistEdge> edges = [Member("guitar", D(1984, 1, 1), null)];

        IReadOnlyList<ArtistEdge> active = LineupIntervalResolver.MembersActiveOn(edges, D(2026, 1, 1));

        Assert.Single(active);
    }

    [Fact]
    public void OpenStartMember_IsActiveBeforeAnyRecordedEnd()
    {
        List<ArtistEdge> edges = [Member("bass", null, D(1990, 6, 1))];

        Assert.Single(LineupIntervalResolver.MembersActiveOn(edges, D(1988, 1, 1)));
        Assert.Empty(LineupIntervalResolver.MembersActiveOn(edges, D(1991, 1, 1)));
    }

    [Fact]
    public void BeginDate_IsInclusive()
    {
        List<ArtistEdge> edges = [Member("vocals", D(1991, 3, 15), D(1995, 3, 15))];

        Assert.Single(LineupIntervalResolver.MembersActiveOn(edges, D(1991, 3, 15)));
    }

    [Fact]
    public void EndDate_IsInclusive()
    {
        List<ArtistEdge> edges = [Member("drums", D(1991, 3, 15), D(1995, 3, 15))];

        Assert.Single(LineupIntervalResolver.MembersActiveOn(edges, D(1995, 3, 15)));
    }

    [Fact]
    public void MemberWhoLeftBeforeDate_IsNotActive()
    {
        List<ArtistEdge> edges = [Member("guitar", D(1988, 1, 1), D(1992, 12, 31))];

        Assert.Empty(LineupIntervalResolver.MembersActiveOn(edges, D(1994, 1, 1)));
    }

    [Fact]
    public void OnlyMembersActiveOnTheDate_AreReturned()
    {
        List<ArtistEdge> edges =
        [
            Member("founder", D(1984, 1, 1), null),
            Member("early", D(1984, 1, 1), D(1987, 1, 1)),
            Member("later", D(1990, 1, 1), null),
        ];

        IReadOnlyList<ArtistEdge> active = LineupIntervalResolver.MembersActiveOn(edges, D(1986, 1, 1));

        Assert.Equal(2, active.Count);
        Assert.Contains(active, e => e.Instruments[0] == "founder");
        Assert.Contains(active, e => e.Instruments[0] == "early");
    }

    [Fact]
    public void NonMembershipEdges_AreIgnored()
    {
        ArtistEdge influence = new()
        {
            Id = Guid.NewGuid(),
            FromId = Guid.NewGuid(),
            ToId = Guid.NewGuid(),
            Kind = EdgeKind.InfluencedBy,
            BeginDate = D(1980, 1, 1),
            EndDate = null,
            Instruments = [],
        };

        Assert.Empty(LineupIntervalResolver.MembersActiveOn([influence], D(2000, 1, 1)));
    }
}
