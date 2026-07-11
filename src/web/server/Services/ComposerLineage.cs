using Grimoire.Library.Models;
using Grimoire.Server.Dtos;

namespace Grimoire.Server.Services;

/// <summary>
/// Pure builder for a composer's lineage graph (movement VII, D11). Turns the raw teacher/student
/// and influence edges around a composer into the <see cref="GraphDto"/> the shared GraphCanvas
/// paints (D18), and canonicalises the pedagogy so each master–apprentice relation is one directed
/// edge.
///
/// <para>
/// MusicBrainz materialises every teacher relation twice — a <c>Teacher</c> edge (master→apprentice)
/// and a <c>Student</c> edge (apprentice→master, classical-data §2). Drawing both would double every
/// line, so this collapses each relation to a single <c>teacher</c> edge master→apprentice, whichever
/// of the pair it holds. Influence (<c>InfluencedBy</c>) is kept as its own directed edge. Nothing is
/// invented: an edge appears only where MusicBrainz/Wikidata recorded one, and a composer with no
/// relations yields an empty graph (the designed empty state, not a lone stub).
/// </para>
/// </summary>
public static class ComposerLineage
{
    /// <summary>Identity of a node in the lineage, resolved once by the caller.</summary>
    public sealed record LineageNode(Guid Id, string Name, ArtistKind Kind, Rank? Rank);

    /// <summary>A raw artist edge, already filtered to the pedagogical/influence kinds.</summary>
    public sealed record LineageEdge(Guid FromId, Guid ToId, EdgeKind Kind);

    /// <summary>
    /// Builds the ego graph. <paramref name="egoId"/> is drawn as the ego (sulphur, DESIGN §5).
    /// Only edges whose endpoints both resolve in <paramref name="nodes"/> are drawn. Teacher and
    /// Student edges collapse to one directed <c>teacher</c> edge master→apprentice; InfluencedBy
    /// becomes a directed <c>influence</c> edge. A node is emitted only if it touches a drawn edge,
    /// so a composer with no lineage produces zero nodes and zero edges.
    /// </summary>
    public static GraphDto BuildGraph(
        Guid egoId,
        IEnumerable<LineageEdge> edges,
        IReadOnlyDictionary<Guid, LineageNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(nodes);

        // Canonical key for a pedagogical relation, direction-independent, so the Teacher edge and
        // its mirror Student edge collapse to the same slot and are emitted once.
        var pedagogy = new HashSet<(Guid Master, Guid Apprentice)>();
        var influence = new HashSet<(Guid From, Guid To)>();

        foreach (LineageEdge edge in edges)
        {
            // Skip edges that reach a node we could not resolve (kept the graph honest).
            if (!nodes.ContainsKey(edge.FromId) || !nodes.ContainsKey(edge.ToId))
            {
                continue;
            }

            switch (edge.Kind)
            {
                case EdgeKind.Teacher:
                    pedagogy.Add((edge.FromId, edge.ToId));
                    break;
                case EdgeKind.Student:
                    // Student is apprentice→master; store as master→apprentice so it dedupes
                    // against the Teacher edge of the same relation.
                    pedagogy.Add((edge.ToId, edge.FromId));
                    break;
                case EdgeKind.InfluencedBy:
                    influence.Add((edge.FromId, edge.ToId));
                    break;
                default:
                    // Not part of a composer's lineage (member/side-project/collaboration).
                    break;
            }
        }

        var edgeDtos = new List<GraphEdgeDto>();
        var referenced = new HashSet<Guid>();

        foreach ((Guid master, Guid apprentice) in pedagogy.OrderBy(p => p.Master).ThenBy(p => p.Apprentice))
        {
            edgeDtos.Add(new GraphEdgeDto(master, apprentice, "teacher", null));
            referenced.Add(master);
            referenced.Add(apprentice);
        }

        foreach ((Guid from, Guid to) in influence.OrderBy(p => p.From).ThenBy(p => p.To))
        {
            edgeDtos.Add(new GraphEdgeDto(from, to, "influence", null));
            referenced.Add(from);
            referenced.Add(to);
        }

        List<GraphNodeDto> nodeDtos = referenced
            .Select(id => nodes[id])
            .OrderBy(n => n.Id)
            .Select(n => new GraphNodeDto(
                n.Id,
                n.Name,
                n.Kind,
                n.Rank,
                n.Id == egoId ? "ego" : "node"))
            .ToList();

        return new GraphDto(nodeDtos, edgeDtos);
    }
}
