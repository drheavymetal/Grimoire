using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Movement IV — Lineage. The blood and the network of the underground, read off the real artist
/// graph (2342 <c>MemberOf</c> edges with dates and instruments, 67 <c>InfluencedBy</c> edges from
/// Wikidata P737): the Bloodline ego graph (B16), Six Degrees of Metal (B19), the diaspora of a
/// broken-up band (B11), the bands a musician played in (B3), the missing link between two bands in
/// embedding space (C5), the Rabbit Hole walk (C8), and the user's own grimoire as a graph (C17).
///
/// <para>
/// Everything is public except the grimoire graph, which is a slice of the caller's own rites.
/// Nothing is invented: a query that finds no path, no diaspora or no neighbour returns an empty
/// result the front renders as a designed empty state (R2).
/// </para>
/// </summary>
[ApiController]
[Route("api/lineage")]
public class LineageController : ControllerBase
{
    private const int MaxNeighbours = 12;
    private const int MaxRabbitHole = 20;
    private const int DefaultRabbitHole = 10;

    private readonly GrimoireDbContext _db;
    private readonly LineageGraph _graph;

    public LineageController(GrimoireDbContext db, LineageGraph graph)
    {
        _db = db;
        _graph = graph;
    }

    // -----------------------------------------------------------------------
    // B16 — Bloodline: the ego graph of one artist, N hops out.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The ego graph of an artist: the bands and people reachable within <paramref name="hops"/>
    /// edges, linked by shared membership and declared influence (B16). Click any node to open its
    /// page. Returns just the ego when the artist has no edges (a designed empty state).
    /// </summary>
    [HttpGet("{id:guid}/bloodline")]
    public async Task<ActionResult<GraphDto>> Bloodline(Guid id, [FromQuery] int hops = 2, CancellationToken ct = default)
    {
        if (!await _db.Artists.AnyAsync(a => a.Id == id, ct))
        {
            return NotFound();
        }

        int depth = Math.Clamp(hops, 1, 4);
        LineageGraphData data = await _graph.LoadAsync(ct);

        IReadOnlySet<Guid> nodeIds = GraphAlgorithms.Neighbourhood(data.ArtistAdjacency, id, depth);

        Dictionary<Guid, GraphNodeDto> nodes = await FetchNodesAsync(nodeIds, ct);
        AssignRole(nodes, id, "ego");

        List<GraphEdgeDto> edges = EdgesWithin(data, nodeIds);

        return Ok(new GraphDto(OrderNodes(nodes.Values, id), edges));
    }

    // -----------------------------------------------------------------------
    // B19 — Six Degrees of Metal: shortest path between two bands.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The shortest chain of shared members connecting two bands (B19): band → member → band → …
    /// <see cref="PathDto.Degrees"/> counts the band-to-band hops. An empty path means the two are
    /// not connected in the graph we hold — an honest "no path", not an error.
    /// </summary>
    [HttpGet("six-degrees")]
    public async Task<ActionResult<PathDto>> SixDegrees([FromQuery] Guid from, [FromQuery] Guid to, CancellationToken ct)
    {
        if (from == Guid.Empty || to == Guid.Empty)
        {
            return BadRequest(new { message = "Both 'from' and 'to' band ids are required." });
        }

        HashSet<Guid> present = (await _db.Artists
                .Where(a => a.Id == from || a.Id == to)
                .Select(a => a.Id)
                .ToListAsync(ct))
            .ToHashSet();

        if (!present.Contains(from) || !present.Contains(to))
        {
            return NotFound(new { message = "One or both of the bands are unknown." });
        }

        LineageGraphData data = await _graph.LoadAsync(ct);

        IReadOnlyList<Guid> pathIds = GraphAlgorithms.ShortestPath(data.ArtistAdjacency, from, to);

        Dictionary<Guid, GraphNodeDto> nodes = await FetchNodesAsync(pathIds.ToHashSet(), ct);
        AssignRole(nodes, from, "source");
        AssignRole(nodes, to, "target");

        List<GraphNodeDto> ordered = pathIds
            .Select(pid => nodes.TryGetValue(pid, out GraphNodeDto? n) ? n : null)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

        int degrees = Math.Max(0, ordered.Count(n => n.Kind == ArtistKind.Group) - 1);

        return Ok(new PathDto(ordered, degrees));
    }

    // -----------------------------------------------------------------------
    // B11 — Diaspora: where a broken-up band's members went next.
    // -----------------------------------------------------------------------

    /// <summary>
    /// When a band breaks up, its members scatter (B11): each member who left (has an end date) and
    /// the bands they joined <b>after</b> leaving. A move is shown only when both dates support it —
    /// no order is invented (R2). Empty when nobody left for a dated later band.
    /// </summary>
    [HttpGet("{id:guid}/diaspora")]
    public async Task<ActionResult<DiasporaDto>> Diaspora(Guid id, CancellationToken ct)
    {
        GraphNodeDto? bandNode = (await FetchNodesAsync(new HashSet<Guid> { id }, ct)).GetValueOrDefault(id);

        if (bandNode is null)
        {
            return NotFound();
        }

        LineageGraphData data = await _graph.LoadAsync(ct);

        // Members who left this band (a known end date).
        var departed = data.MemberEdges
            .Where(e => e.BandId == id && e.End is not null)
            .Select(e => new { e.PersonId, Left = e.End })
            .ToList();

        List<DiasporaMemberDto> members = [];

        // Node metadata for every person and destination band we will name.
        HashSet<Guid> involved = departed.Select(d => d.PersonId).ToHashSet();
        foreach (var d in departed)
        {
            foreach (MemberEdge other in data.MemberEdges.Where(e => e.PersonId == d.PersonId && e.BandId != id))
            {
                if (LineageMath.WentAfterLeaving(d.Left, other.Begin))
                {
                    involved.Add(other.BandId);
                }
            }
        }

        Dictionary<Guid, GraphNodeDto> meta = await FetchNodesAsync(involved, ct);

        foreach (var d in departed)
        {
            List<DiasporaDestinationDto> destinations = data.MemberEdges
                .Where(e => e.PersonId == d.PersonId && e.BandId != id && LineageMath.WentAfterLeaving(d.Left, e.Begin))
                .Select(e => meta.TryGetValue(e.BandId, out GraphNodeDto? bn)
                    ? new DiasporaDestinationDto(bn.Id, bn.Name, bn.Rank, e.Begin)
                    : null)
                .Where(x => x is not null)
                .Select(x => x!)
                .DistinctBy(x => x.BandId)
                .OrderBy(x => x.JoinedYear ?? DateOnly.MaxValue)
                .ToList();

            if (destinations.Count == 0 || !meta.TryGetValue(d.PersonId, out GraphNodeDto? person))
            {
                continue;
            }

            members.Add(new DiasporaMemberDto(person.Id, person.Name, d.Left, destinations));
        }

        members = members.OrderByDescending(m => m.Destinations.Count).ThenBy(m => m.MemberName).ToList();

        return Ok(new DiasporaDto(bandNode, members));
    }

    // -----------------------------------------------------------------------
    // B3 — Bands a musician played in.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every band a musician played in (B3), with their stint and instruments in each. Empty when the
    /// person has no memberships on record.
    /// </summary>
    [HttpGet("{id:guid}/bands")]
    public async Task<ActionResult<MemberBandsDto>> BandsOfMember(Guid id, CancellationToken ct)
    {
        GraphNodeDto? memberNode = (await FetchNodesAsync(new HashSet<Guid> { id }, ct)).GetValueOrDefault(id);

        if (memberNode is null)
        {
            return NotFound();
        }

        LineageGraphData data = await _graph.LoadAsync(ct);

        List<MemberEdge> memberships = data.MemberEdges.Where(e => e.PersonId == id).ToList();

        Dictionary<Guid, GraphNodeDto> bandMeta = await FetchNodesAsync(memberships.Select(m => m.BandId).ToHashSet(), ct);

        List<MemberBandDto> bands = memberships
            .Select(m => bandMeta.TryGetValue(m.BandId, out GraphNodeDto? bn)
                ? new MemberBandDto(bn.Id, bn.Name, bn.Kind, bn.Rank, m.Begin, m.End, m.Instruments)
                : null)
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderBy(b => b.BeginDate ?? DateOnly.MaxValue)
            .ThenBy(b => b.BandName)
            .ToList();

        return Ok(new MemberBandsDto(memberNode, bands));
    }

    // -----------------------------------------------------------------------
    // C5 — The missing link: what lives between two bands in embedding space.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The bands that live <b>between</b> two others (C5): interpolate the midpoint of their centred
    /// embeddings and return its nearest neighbours (the two endpoints excluded). Nobody else can
    /// answer this today. 422 if either band has no embedding yet.
    /// </summary>
    [HttpGet("missing-link")]
    public async Task<ActionResult<MissingLinkDto>> MissingLink([FromQuery] Guid from, [FromQuery] Guid to, CancellationToken ct)
    {
        if (from == Guid.Empty || to == Guid.Empty || from == to)
        {
            return BadRequest(new { message = "Two distinct band ids are required." });
        }

        var ends = await _db.Artists
            .Where(a => a.Id == from || a.Id == to)
            .Select(a => new { a.Id, a.Embedding })
            .ToListAsync(ct);

        var fromRow = ends.FirstOrDefault(e => e.Id == from);
        var toRow = ends.FirstOrDefault(e => e.Id == to);

        if (fromRow is null || toRow is null)
        {
            return NotFound(new { message = "One or both of the bands are unknown." });
        }

        if (fromRow.Embedding is null || toRow.Embedding is null)
        {
            return UnprocessableEntity(new { message = "One or both bands have no embedding yet, so the space between them cannot be interpolated." });
        }

        // The midpoint of two already-centred embeddings is centred too (D26/D31): never re-centre.
        float[] midpoint = LineageMath.Midpoint(fromRow.Embedding.ToArray(), toRow.Embedding.ToArray());
        Vector mid = new(midpoint);

        List<MissingLinkNeighbourDto> between = await _db.Artists
            .Discoverable()
            .Where(a => a.Id != from && a.Id != to)
            .OrderBy(a => a.Embedding!.CosineDistance(mid))
            .Take(MaxNeighbours)
            .Select(a => new MissingLinkNeighbourDto(a.Id, a.Name, a.Kind, a.Rank, a.Embedding!.CosineDistance(mid)))
            .ToListAsync(ct);

        Dictionary<Guid, GraphNodeDto> endMeta = await FetchNodesAsync(new HashSet<Guid> { from, to }, ct);

        return Ok(new MissingLinkDto(endMeta[from], endMeta[to], between));
    }

    // -----------------------------------------------------------------------
    // C8 — Rabbit Hole: a guided walk through the lineage.
    // -----------------------------------------------------------------------

    /// <summary>
    /// A guided walk through the lineage (C8): starting from a band, each step follows a shared
    /// member or an influence to a band not yet seen. It stops early at a dead end — an honest
    /// out-of-graph, not a padded chain.
    /// </summary>
    [HttpGet("{id:guid}/rabbit-hole")]
    public async Task<ActionResult<RabbitHoleDto>> RabbitHole(Guid id, [FromQuery] int length = DefaultRabbitHole, CancellationToken ct = default)
    {
        if (!await _db.Artists.AnyAsync(a => a.Id == id, ct))
        {
            return NotFound();
        }

        int steps = Math.Clamp(length, 2, MaxRabbitHole);
        LineageGraphData data = await _graph.LoadAsync(ct);

        IReadOnlyList<Guid> walk = GraphAlgorithms.Walk(
            data.BandAdjacency,
            id,
            steps,
            candidates => candidates[Random.Shared.Next(candidates.Count)]);

        Dictionary<Guid, GraphNodeDto> nodes = await FetchNodesAsync(walk.ToHashSet(), ct);

        List<GraphNodeDto> ordered = walk
            .Select(wid => nodes.TryGetValue(wid, out GraphNodeDto? n) ? n : null)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

        return Ok(new RabbitHoleDto(ordered));
    }

    // -----------------------------------------------------------------------
    // C17 — Your grimoire as a graph.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The caller's own grimoire as a graph (C17): only the bands they have summoned, and the edges
    /// between those bands (shared members and influence). Requires a signed-in user. Empty when
    /// nothing has been summoned yet.
    /// </summary>
    [Authorize]
    [HttpGet("grimoire-graph")]
    public async Task<ActionResult<GraphDto>> GrimoireGraph(CancellationToken ct)
    {
        Guid userId = CurrentUserId();

        HashSet<Guid> summoned = (await _db.Rites
                .Where(r => r.UserId == userId && r.State == RiteState.Summoned)
                .Select(r => r.ArtistId)
                .ToListAsync(ct))
            .ToHashSet();

        Dictionary<Guid, GraphNodeDto> nodes = await FetchNodesAsync(summoned, ct);

        // The grimoire graph is a BAND graph: two summoned bands are joined when they share a member
        // or one influenced the other. The bridging musician is not itself in the grimoire, so a raw
        // person→band edge would never surface here — the connection has to be drawn band to band.
        List<GraphEdgeDto> edges = [];

        if (summoned.Count > 1)
        {
            LineageGraphData data = await _graph.LoadAsync(ct);
            edges = await BandEdgesWithinAsync(data, summoned, ct);
        }

        return Ok(new GraphDto(nodes.Values.OrderBy(n => n.Name).ToList(), edges));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Fetches display metadata (name, kind, rank) for a set of artist ids, keyed by id.</summary>
    private async Task<Dictionary<Guid, GraphNodeDto>> FetchNodesAsync(IReadOnlySet<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Artists
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new GraphNodeDto(a.Id, a.Name, a.Kind, a.Rank, "node"))
            .ToDictionaryAsync(n => n.Id, ct);
    }

    /// <summary>Rewrites one node's role in place (records are immutable, so replace the entry).</summary>
    private static void AssignRole(Dictionary<Guid, GraphNodeDto> nodes, Guid id, string role)
    {
        if (nodes.TryGetValue(id, out GraphNodeDto? node))
        {
            nodes[id] = node with { Role = role };
        }
    }

    /// <summary>All member and influence edges whose endpoints both fall inside the node set.</summary>
    private static List<GraphEdgeDto> EdgesWithin(LineageGraphData data, IReadOnlySet<Guid> nodeIds)
    {
        List<GraphEdgeDto> edges = [];

        foreach (MemberEdge e in data.MemberEdges)
        {
            if (nodeIds.Contains(e.PersonId) && nodeIds.Contains(e.BandId))
            {
                string? label = e.Instruments.Length > 0 ? string.Join(", ", e.Instruments) : null;
                edges.Add(new GraphEdgeDto(e.PersonId, e.BandId, "member", label));
            }
        }

        foreach ((Guid from, Guid to) in data.InfluenceEdges)
        {
            if (nodeIds.Contains(from) && nodeIds.Contains(to))
            {
                edges.Add(new GraphEdgeDto(from, to, "influence", null));
            }
        }

        return edges;
    }

    /// <summary>
    /// Band-to-band edges within a set of bands (C17): two bands are joined when a person played in
    /// both (label = the shared members) or one influenced the other. Unlike <see cref="EdgesWithin"/>
    /// this draws the connection directly between bands, since the bridging musician is not in the set.
    /// </summary>
    private async Task<List<GraphEdgeDto>> BandEdgesWithinAsync(LineageGraphData data, IReadOnlySet<Guid> bandIds, CancellationToken ct)
    {
        // For each person, which of the given bands they played in — a person in two of them bridges.
        Dictionary<Guid, List<Guid>> bandsByPerson = [];
        foreach (MemberEdge e in data.MemberEdges)
        {
            if (bandIds.Contains(e.BandId))
            {
                (bandsByPerson.TryGetValue(e.PersonId, out List<Guid>? bs) ? bs : bandsByPerson[e.PersonId] = []).Add(e.BandId);
            }
        }

        // Accumulate the bridging people per unordered band pair.
        Dictionary<(Guid Lo, Guid Hi), HashSet<Guid>> shared = [];
        foreach ((Guid person, List<Guid> bands) in bandsByPerson)
        {
            List<Guid> distinct = bands.Distinct().ToList();
            for (int i = 0; i < distinct.Count; i++)
            {
                for (int j = i + 1; j < distinct.Count; j++)
                {
                    (Guid lo, Guid hi) = distinct[i].CompareTo(distinct[j]) < 0 ? (distinct[i], distinct[j]) : (distinct[j], distinct[i]);
                    (shared.TryGetValue((lo, hi), out HashSet<Guid>? set) ? set : shared[(lo, hi)] = []).Add(person);
                }
            }
        }

        // Names for the bridging people, for the edge labels.
        HashSet<Guid> bridgeIds = shared.Values.SelectMany(s => s).ToHashSet();
        Dictionary<Guid, string> bridgeNames = bridgeIds.Count == 0
            ? []
            : await _db.Artists
                .AsNoTracking()
                .Where(a => bridgeIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name })
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        List<GraphEdgeDto> edges = [];

        foreach (((Guid lo, Guid hi), HashSet<Guid> people) in shared)
        {
            string label = string.Join(", ", people.Select(p => bridgeNames.GetValueOrDefault(p, string.Empty)).Where(n => n.Length > 0));
            edges.Add(new GraphEdgeDto(lo, hi, "member", label.Length > 0 ? label : null));
        }

        // Influence edges between two summoned bands, when neither is already a shared-member pair.
        foreach ((Guid from, Guid to) in data.InfluenceEdges)
        {
            if (bandIds.Contains(from) && bandIds.Contains(to))
            {
                (Guid lo, Guid hi) = from.CompareTo(to) < 0 ? (from, to) : (to, from);
                if (!shared.ContainsKey((lo, hi)))
                {
                    edges.Add(new GraphEdgeDto(from, to, "influence", null));
                }
            }
        }

        return edges;
    }

    /// <summary>Puts the ego first, then the rest by name — a stable order for the client.</summary>
    private static List<GraphNodeDto> OrderNodes(IEnumerable<GraphNodeDto> nodes, Guid egoId)
    {
        return nodes
            .OrderByDescending(n => n.Id == egoId)
            .ThenBy(n => n.Name)
            .ToList();
    }

    private Guid CurrentUserId()
    {
        string? sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(sub, out Guid id))
        {
            return id;
        }

        throw new InvalidOperationException("Authenticated request carries no usable subject claim.");
    }
}
