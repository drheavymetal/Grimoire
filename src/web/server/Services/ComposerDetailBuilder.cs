using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Services;

/// <summary>
/// Builds the <see cref="ComposerDetailDto"/> for one composer (movement VII, D11): the grouped
/// list of works (the hero) plus the two lineages (teacher/student and influence). It reads the
/// real <c>works</c> and <c>artist_edges</c> rows; a composer with few works or no lineage yields a
/// small or empty result, which the front renders as a designed empty state (sparse lineage is real
/// — only 12 teacher/student edges in the whole corpus).
///
/// <para>
/// Deliberately carries NO Gantt, NO members and NO rank: the composer model has none of those
/// (D11 — classical listeners lie). Identity (name, country, bio) is served by the shared artist
/// detail; this adds only the composer-specific body.
/// </para>
/// </summary>
public sealed class ComposerDetailBuilder
{
    // The edge kinds that make up a composer's lineage: pedagogy and declared influence.
    private static readonly EdgeKind[] LineageKinds =
    [
        EdgeKind.Teacher,
        EdgeKind.Student,
        EdgeKind.InfluencedBy,
    ];

    private readonly GrimoireDbContext _db;

    public ComposerDetailBuilder(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>Returns the composer detail, or null when the id is unknown.</summary>
    public async Task<ComposerDetailDto?> BuildAsync(Guid id, CancellationToken ct)
    {
        bool exists = await _db.Artists.AsNoTracking().AnyAsync(a => a.Id == id, ct);
        if (!exists)
        {
            return null;
        }

        // --- Works, grouped by kind (null => unclassified, kept, D11). ---
        List<WorkGrouping.WorkRow> workRows = await _db.Works
            .AsNoTracking()
            .Where(w => w.ComposerId == id)
            .Select(w => new WorkGrouping.WorkRow(w.Id, w.Mbid, w.Title, w.Kind))
            .ToListAsync(ct);

        IReadOnlyList<WorkGroupDto> workGroups = WorkGrouping.Group(workRows);

        // --- Lineage: the ego's own teacher/student/influence edges (1 hop out). ---
        var egoEdges = await _db.ArtistEdges
            .AsNoTracking()
            .Where(e => (e.FromId == id || e.ToId == id) && LineageKinds.Contains(e.Kind))
            .Select(e => new { e.FromId, e.ToId, e.Kind })
            .ToListAsync(ct);

        // The neighbourhood: the ego plus everyone one lineage hop away. The graph draws edges
        // among this set, so a teacher and a student of the same composer (e.g. Fauré and Glass
        // around Boulanger) both appear and the chain is legible.
        HashSet<Guid> nodeSet = [id];
        foreach (var e in egoEdges)
        {
            nodeSet.Add(e.FromId == id ? e.ToId : e.FromId);
        }

        // Every lineage edge with both endpoints inside the neighbourhood.
        var graphEdges = await _db.ArtistEdges
            .AsNoTracking()
            .Where(e => LineageKinds.Contains(e.Kind)
                && nodeSet.Contains(e.FromId)
                && nodeSet.Contains(e.ToId))
            .Select(e => new { e.FromId, e.ToId, e.Kind })
            .ToListAsync(ct);

        Dictionary<Guid, ComposerLineage.LineageNode> nodeLookup = await _db.Artists
            .AsNoTracking()
            .Where(a => nodeSet.Contains(a.Id))
            .Select(a => new ComposerLineage.LineageNode(a.Id, a.Name, a.Kind, a.Rank))
            .ToDictionaryAsync(n => n.Id, n => n, ct);

        GraphDto graph = ComposerLineage.BuildGraph(
            id,
            graphEdges.Select(e => new ComposerLineage.LineageEdge(e.FromId, e.ToId, e.Kind)),
            nodeLookup);

        // Textual lists of the immediate relations, for a plain clickable reading beside the graph.
        // "Studied with" = the ego's Student edges (apprentice→master); "Taught" = its Teacher edges.
        IReadOnlyList<ComposerLinkDto> teachers = LinksFor(egoEdges
            .Where(e => e.FromId == id && e.Kind == EdgeKind.Student)
            .Select(e => e.ToId), nodeLookup);

        IReadOnlyList<ComposerLinkDto> students = LinksFor(egoEdges
            .Where(e => e.FromId == id && e.Kind == EdgeKind.Teacher)
            .Select(e => e.ToId), nodeLookup);

        IReadOnlyList<ComposerLinkDto> influences = LinksFor(egoEdges
            .Where(e => e.FromId == id && e.Kind == EdgeKind.InfluencedBy)
            .Select(e => e.ToId), nodeLookup);

        var lineage = new ComposerLineageDto(teachers, students, influences, graph);

        return new ComposerDetailDto(workRows.Count, workGroups, lineage);
    }

    private static IReadOnlyList<ComposerLinkDto> LinksFor(
        IEnumerable<Guid> ids,
        IReadOnlyDictionary<Guid, ComposerLineage.LineageNode> nodes)
    {
        return ids
            .Distinct()
            .Where(nodes.ContainsKey)
            .Select(nodeId => new ComposerLinkDto(nodeId, nodes[nodeId].Name))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
