using Grimoire.Library.Models;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// "The disc where everything changed" (feature B12). These bite on the turnover count around a
/// release: a stable lineup scores zero, and the release straddling a member swap scores the two
/// changes. Built on the same interval logic as the Gantt (LineupIntervalResolver).
/// </summary>
public class LineupTurnoverTests
{
    private static readonly Guid Band = Guid.Parse("b0000000-0000-0000-0000-000000000000");
    private static readonly Guid M1 = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid M2 = Guid.Parse("20000000-0000-0000-0000-000000000000");
    private static readonly Guid M3 = Guid.Parse("30000000-0000-0000-0000-000000000000");

    private static ArtistEdge Member(Guid member, string? begin, string? end)
    {
        return new ArtistEdge
        {
            Id = Guid.NewGuid(),
            FromId = Band,
            ToId = member,
            Kind = EdgeKind.MemberOf,
            BeginDate = begin is null ? null : DateOnly.Parse(begin),
            EndDate = end is null ? null : DateOnly.Parse(end),
        };
    }

    // A founding pair, then M2 leaves and M3 joins in the middle of 1995.
    private static IReadOnlyList<ArtistEdge> Lineup()
    {
        return
        [
            Member(M1, "1990-01-01", null),
            Member(M2, "1990-01-01", "1995-06-01"),
            Member(M3, "1995-03-01", null),
        ];
    }

    [Fact]
    public void Around_StableLineup_ScoresZero()
    {
        // 1992: both founders are in, before and after — nothing changed.
        var t = LineupTurnover.Around(Band, Guid.NewGuid(), new DateOnly(1992, 1, 1), Lineup());
        Assert.Equal(0, t.Score);
    }

    [Fact]
    public void Around_MemberSwap_CountsBothJoinAndLeave()
    {
        Guid release = Guid.NewGuid();
        var t = LineupTurnover.Around(Band, release, new DateOnly(1995, 6, 1), Lineup());

        Assert.Equal(2, t.Score);
        Assert.Equal(new[] { M3 }, t.Joined);
        Assert.Equal(new[] { M2 }, t.Left);
    }

    [Fact]
    public void MostPivotal_PicksTheReleaseWithTheMostChurn()
    {
        Guid stable = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
        Guid swap = Guid.Parse("cccccccc-0000-0000-0000-000000000000");

        var releases = new List<(Guid, DateOnly)>
        {
            (stable, new DateOnly(1992, 1, 1)),
            (swap, new DateOnly(1995, 6, 1)),
        };

        var pivotal = LineupTurnover.MostPivotal(Band, releases, Lineup());

        Assert.NotNull(pivotal);
        Assert.Equal(swap, pivotal!.ReleaseId);
        Assert.Equal(2, pivotal.Score);
    }

    [Fact]
    public void MostPivotal_ReturnsNull_WhenNothingEverChanges()
    {
        var releases = new List<(Guid, DateOnly)>
        {
            (Guid.NewGuid(), new DateOnly(1991, 1, 1)),
            (Guid.NewGuid(), new DateOnly(1992, 1, 1)),
        };

        Assert.Null(LineupTurnover.MostPivotal(Band, releases, Lineup()));
    }
}
