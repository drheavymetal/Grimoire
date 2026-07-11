using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The composer master–apprentice lineage graph (movement VII, D11). These bite on the one thing
/// that matters: MusicBrainz records every teacher relation twice (a Teacher edge and its mirror
/// Student edge), and the graph must collapse each to a single directed edge master→apprentice —
/// never draw the same relation twice, never invent a relation that was not recorded.
/// </summary>
public class ComposerLineageTests
{
    private static readonly Guid Faure = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Boulanger = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Glass = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Stravinsky = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static Dictionary<Guid, ComposerLineage.LineageNode> Nodes()
    {
        return new[]
        {
            new ComposerLineage.LineageNode(Faure, "Gabriel Fauré", ArtistKind.Person, null),
            new ComposerLineage.LineageNode(Boulanger, "Nadia Boulanger", ArtistKind.Person, null),
            new ComposerLineage.LineageNode(Glass, "Philip Glass", ArtistKind.Person, null),
            new ComposerLineage.LineageNode(Stravinsky, "Igor Stravinsky", ArtistKind.Person, null),
        }.ToDictionary(n => n.Id, n => n);
    }

    [Fact]
    public void BuildGraph_CollapsesTeacherAndStudentMirrorsToOneDirectedEdge()
    {
        // The full mirrored pair for Fauré→Boulanger: a Teacher edge AND its reverse Student edge.
        var edges = new[]
        {
            new ComposerLineage.LineageEdge(Faure, Boulanger, EdgeKind.Teacher),
            new ComposerLineage.LineageEdge(Boulanger, Faure, EdgeKind.Student),
        };

        GraphDto graph = ComposerLineage.BuildGraph(Boulanger, edges, Nodes());

        // Exactly one edge, directed master→apprentice. Drop the dedup and this becomes two.
        GraphEdgeDto edge = Assert.Single(graph.Edges);
        Assert.Equal("teacher", edge.Kind);
        Assert.Equal(Faure, edge.Source);
        Assert.Equal(Boulanger, edge.Target);
    }

    [Fact]
    public void BuildGraph_ShowsBothEndsOfTheChainAroundTheEgo()
    {
        // Boulanger studied with Fauré and taught Glass: the three-generation chain must be legible
        // from Boulanger's page (the gate's Nadia Boulanger check).
        var edges = new[]
        {
            new ComposerLineage.LineageEdge(Faure, Boulanger, EdgeKind.Teacher),
            new ComposerLineage.LineageEdge(Boulanger, Glass, EdgeKind.Teacher),
        };

        GraphDto graph = ComposerLineage.BuildGraph(Boulanger, edges, Nodes());

        Assert.Equal(3, graph.Nodes.Count);
        Assert.Contains(graph.Edges, e => e.Source == Faure && e.Target == Boulanger);
        Assert.Contains(graph.Edges, e => e.Source == Boulanger && e.Target == Glass);
        // The ego is marked so the painter draws it in sulphur.
        Assert.Equal("ego", Assert.Single(graph.Nodes, n => n.Id == Boulanger).Role);
        Assert.All(graph.Nodes.Where(n => n.Id != Boulanger), n => Assert.Equal("node", n.Role));
    }

    [Fact]
    public void BuildGraph_KeepsInfluenceAsItsOwnDirectedEdge()
    {
        var edges = new[]
        {
            new ComposerLineage.LineageEdge(Glass, Stravinsky, EdgeKind.InfluencedBy),
        };

        GraphDto graph = ComposerLineage.BuildGraph(Glass, edges, Nodes());

        GraphEdgeDto edge = Assert.Single(graph.Edges);
        Assert.Equal("influence", edge.Kind);
        Assert.Equal(Glass, edge.Source);
        Assert.Equal(Stravinsky, edge.Target);
    }

    [Fact]
    public void BuildGraph_NoLineageYieldsEmptyGraph()
    {
        // A composer with no teacher/student/influence edge gets an empty graph — the front's
        // designed empty state, not a lone stub node.
        GraphDto graph = ComposerLineage.BuildGraph(Boulanger, [], Nodes());

        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void BuildGraph_SkipsEdgesReachingUnresolvedNodes()
    {
        Guid unknown = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var edges = new[]
        {
            new ComposerLineage.LineageEdge(Boulanger, unknown, EdgeKind.Teacher),
        };

        GraphDto graph = ComposerLineage.BuildGraph(Boulanger, edges, Nodes());

        // The endpoint is not in the node lookup, so nothing is drawn — no invented node.
        Assert.Empty(graph.Edges);
        Assert.Empty(graph.Nodes);
    }
}
