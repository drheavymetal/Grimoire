using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The pure graph traversals behind movement IV: Six Degrees' BFS (B19), the Bloodline ego
/// neighbourhood (B16) and the Rabbit Hole walk (C8). These bite — the assertions pin down the
/// shortest-path, the hop boundary and the no-repeat rule that make each feature true, not just
/// plausible.
/// </summary>
public class GraphAlgorithmsTests
{
    // A small graph:  a — b — c — d,   b — e.
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-0000000000d4");
    private static readonly Guid E = Guid.Parse("00000000-0000-0000-0000-0000000000e5");
    private static readonly Guid Island = Guid.Parse("00000000-0000-0000-0000-00000000f00f");

    private static IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Graph()
    {
        return new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [A] = [B],
            [B] = [A, C, E],
            [C] = [B, D],
            [D] = [C],
            [E] = [B],
            [Island] = [],
        };
    }

    [Fact]
    public void ShortestPath_ReturnsTheFewestEdgesPath()
    {
        IReadOnlyList<Guid> path = GraphAlgorithms.ShortestPath(Graph(), A, D);

        // a — b — c — d is the only, and shortest, path. Order matters: endpoints included.
        Assert.Equal(new[] { A, B, C, D }, path);
    }

    [Fact]
    public void ShortestPath_PrefersTheShorterOfTwoRoutes()
    {
        // Add a shortcut a — c so both a-b-c and a-c reach c; BFS must pick a-c (fewer edges).
        var graph = new Dictionary<Guid, IReadOnlyList<Guid>>
        {
            [A] = [B, C],
            [B] = [A, C],
            [C] = [A, B],
        };

        IReadOnlyList<Guid> path = GraphAlgorithms.ShortestPath(graph, A, C);

        Assert.Equal(new[] { A, C }, path);
    }

    [Fact]
    public void ShortestPath_ReturnsEmptyWhenDisconnected()
    {
        Assert.Empty(GraphAlgorithms.ShortestPath(Graph(), A, Island));
    }

    [Fact]
    public void ShortestPath_SameNodeIsASingleStep()
    {
        Assert.Equal(new[] { A }, GraphAlgorithms.ShortestPath(Graph(), A, A));
    }

    [Fact]
    public void Neighbourhood_OneHopIsTheDirectNeighboursPlusEgo()
    {
        IReadOnlySet<Guid> ball = GraphAlgorithms.Neighbourhood(Graph(), B, 1);

        Assert.Equal(new HashSet<Guid> { B, A, C, E }, ball);
        // d is two hops from b — it must NOT be in the one-hop ball (the boundary is load-bearing).
        Assert.DoesNotContain(D, ball);
    }

    [Fact]
    public void Neighbourhood_TwoHopsReachesTheSecondRing()
    {
        IReadOnlySet<Guid> ball = GraphAlgorithms.Neighbourhood(Graph(), A, 2);

        // a's two-hop ball: a, b (1 hop), c and e (2 hops). d is three hops away — excluded.
        Assert.Equal(new HashSet<Guid> { A, B, C, E }, ball);
        Assert.DoesNotContain(D, ball);
    }

    [Fact]
    public void Neighbourhood_ZeroHopsIsJustTheEgo()
    {
        Assert.Equal(new HashSet<Guid> { A }, GraphAlgorithms.Neighbourhood(Graph(), A, 0));
    }

    [Fact]
    public void Walk_NeverRepeatsANode()
    {
        // Deterministic chooser: always the first candidate.
        IReadOnlyList<Guid> walk = GraphAlgorithms.Walk(Graph(), A, 10, candidates => candidates[0]);

        Assert.Equal(walk.Count, walk.Distinct().Count());
        Assert.Equal(A, walk[0]);
    }

    [Fact]
    public void Walk_StopsAtADeadEndBeforeRequestedLength()
    {
        // From a, first candidate is b, then from b the first UNVISITED is c, then d, then dead end.
        IReadOnlyList<Guid> walk = GraphAlgorithms.Walk(Graph(), A, 10, candidates => candidates[0]);

        // a → b → c → d, then d's only neighbour c is visited: the walk stops at 4, not pads to 10.
        Assert.Equal(new[] { A, B, C, D }, walk);
    }

    [Fact]
    public void Walk_RespectsTheRequestedLength()
    {
        IReadOnlyList<Guid> walk = GraphAlgorithms.Walk(Graph(), A, 2, candidates => candidates[0]);

        Assert.Equal(2, walk.Count);
    }
}
