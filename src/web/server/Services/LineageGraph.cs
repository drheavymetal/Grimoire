using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Services;

/// <summary>A membership edge as the lineage features need it: a person, the band, and their stint.</summary>
public sealed record MemberEdge(Guid PersonId, Guid BandId, string[] Instruments, DateOnly? Begin, DateOnly? End);

/// <summary>
/// The whole artist graph loaded into memory (movement IV). The corpus is small (~2.5k artists,
/// ~2.4k edges), so a single load per request is cheap and lets the pure graph algorithms
/// (<see cref="GraphAlgorithms"/>) run without touching the database.
///
/// <para>Two adjacency views are exposed, both undirected:</para>
/// <list type="bullet">
/// <item><b>Artist adjacency</b> — persons and bands, linked by <c>MemberOf</c> (person↔band) and
/// <c>InfluencedBy</c> (band↔band). Bloodline (B16) grows an ego neighbourhood over it; Six Degrees
/// (B19) walks it band → person → band so the bridging musicians show in the path.</item>
/// <item><b>Band adjacency</b> — bands only, linked when they share a member or an influence edge.
/// The Rabbit Hole (C8) walks it and the grimoire graph (C17) is a slice of it.</item>
/// </list>
/// </summary>
public sealed class LineageGraphData
{
    public required IReadOnlyList<MemberEdge> MemberEdges { get; init; }

    public required IReadOnlyList<(Guid From, Guid To)> InfluenceEdges { get; init; }

    public required IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> ArtistAdjacency { get; init; }

    public required IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> BandAdjacency { get; init; }
}

/// <summary>
/// Loads the artist graph from <c>artist_edges</c> and builds the adjacency views the lineage
/// features run on. Scoped: one load per request.
/// </summary>
public sealed class LineageGraph
{
    private readonly GrimoireDbContext _db;

    public LineageGraph(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>Loads every membership and influence edge and derives both adjacency views.</summary>
    public async Task<LineageGraphData> LoadAsync(CancellationToken ct)
    {
        List<MemberEdge> memberEdges = await _db.ArtistEdges
            .AsNoTracking()
            .Where(e => e.Kind == EdgeKind.MemberOf)
            .Select(e => new MemberEdge(e.FromId, e.ToId, e.Instruments, e.BeginDate, e.EndDate))
            .ToListAsync(ct);

        List<(Guid From, Guid To)> influenceEdges = (await _db.ArtistEdges
                .AsNoTracking()
                .Where(e => e.Kind == EdgeKind.InfluencedBy)
                .Select(e => new { e.FromId, e.ToId })
                .ToListAsync(ct))
            .Select(e => (e.FromId, e.ToId))
            .ToList();

        Dictionary<Guid, HashSet<Guid>> artist = [];
        Dictionary<Guid, HashSet<Guid>> band = [];

        void LinkArtist(Guid a, Guid b)
        {
            if (a == b)
            {
                return;
            }

            (artist.TryGetValue(a, out HashSet<Guid>? sa) ? sa : artist[a] = []).Add(b);
            (artist.TryGetValue(b, out HashSet<Guid>? sb) ? sb : artist[b] = []).Add(a);
        }

        void LinkBand(Guid a, Guid b)
        {
            if (a == b)
            {
                return;
            }

            (band.TryGetValue(a, out HashSet<Guid>? sa) ? sa : band[a] = []).Add(b);
            (band.TryGetValue(b, out HashSet<Guid>? sb) ? sb : band[b] = []).Add(a);
        }

        // Person↔band edges, and the bands each person belongs to (for band-to-band adjacency).
        Dictionary<Guid, List<Guid>> bandsByPerson = [];
        foreach (MemberEdge e in memberEdges)
        {
            LinkArtist(e.PersonId, e.BandId);
            (bandsByPerson.TryGetValue(e.PersonId, out List<Guid>? bs) ? bs : bandsByPerson[e.PersonId] = []).Add(e.BandId);
        }

        // Two bands are adjacent when a person played in both.
        foreach (List<Guid> bands in bandsByPerson.Values)
        {
            for (int i = 0; i < bands.Count; i++)
            {
                for (int j = i + 1; j < bands.Count; j++)
                {
                    LinkBand(bands[i], bands[j]);
                }
            }
        }

        // Influence edges link two bands in both graphs.
        foreach ((Guid from, Guid to) in influenceEdges)
        {
            LinkArtist(from, to);
            LinkBand(from, to);
        }

        return new LineageGraphData
        {
            MemberEdges = memberEdges,
            InfluenceEdges = influenceEdges,
            ArtistAdjacency = Freeze(artist),
            BandAdjacency = Freeze(band),
        };
    }

    private static IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Freeze(Dictionary<Guid, HashSet<Guid>> map)
    {
        Dictionary<Guid, IReadOnlyList<Guid>> frozen = new(map.Count);

        foreach ((Guid key, HashSet<Guid> value) in map)
        {
            frozen[key] = value.ToList();
        }

        return frozen;
    }
}
