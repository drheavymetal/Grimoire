using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Movement V — the split graph (C9): the real social network of the underground, where two bands
/// share one physical record. Splits are titled by naming their bands with a slash ("Xasthur /
/// Leviathan"), so a split edge is drawn when the release's owning band appears in its own title and
/// another named band resolves to the corpus by exact name. Reuses the shared graph engine (D18).
///
/// <para>
/// Honestly sparse: most split partners are not in this ~2.5k corpus, and MusicBrainz models
/// splits as one owning artist (D29), so the graph is small by data reality, not by bug. The front
/// renders a designed empty state when nothing resolves. Matching is exact-normalised (the same
/// conservative matcher as the ETL, D25): a miss drops the edge rather than inventing the wrong band.
/// </para>
/// </summary>
[ApiController]
[Route("api/splits")]
public class SplitsController : ControllerBase
{
    private readonly GrimoireDbContext _db;

    public SplitsController(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The split graph (C9): bands joined when they shared a split release. Empty when no split title
    /// resolves both sides to the corpus — an honest reflection of coverage, not an error.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<GraphDto>> Graph(CancellationToken ct = default)
    {
        // Every band by normalised name, to resolve the partners named in a split title.
        var bands = await _db.Artists
            .AsNoTracking()
            .Where(a => a.Kind == ArtistKind.Group)
            .Select(a => new { a.Id, a.Name, a.Rank })
            .ToListAsync(ct);

        Dictionary<string, Guid> byName = [];
        Dictionary<Guid, GraphNodeDto> nodeById = [];
        foreach (var b in bands)
        {
            string key = NameMatch.Normalize(b.Name);
            byName.TryAdd(key, b.Id);
            nodeById[b.Id] = new GraphNodeDto(b.Id, b.Name, ArtistKind.Group, b.Rank, "node");
        }

        // Slash-titled releases, with the owning band's name.
        var splitReleases = await _db.Releases
            .AsNoTracking()
            .Where(r => r.Title.Contains(" / ") && r.Artist != null)
            .Select(r => new { r.Title, r.ArtistId, OwnerName = r.Artist!.Name })
            .ToListAsync(ct);

        HashSet<Guid> usedNodes = [];
        Dictionary<(Guid Lo, Guid Hi), List<string>> edges = [];

        foreach (var r in splitReleases)
        {
            IReadOnlyList<string> parts = SplitTitle.Parts(r.Title);
            string ownerKey = NameMatch.Normalize(r.OwnerName);

            // Require the owner to be one of the named parts: that confirms a band-split title, not an
            // album whose title merely contains a slash.
            if (!parts.Any(p => NameMatch.Normalize(p) == ownerKey))
            {
                continue;
            }

            foreach (string part in parts)
            {
                string partKey = NameMatch.Normalize(part);
                if (partKey == ownerKey || !byName.TryGetValue(partKey, out Guid partnerId) || partnerId == r.ArtistId)
                {
                    continue;
                }

                (Guid lo, Guid hi) = r.ArtistId.CompareTo(partnerId) < 0
                    ? (r.ArtistId, partnerId)
                    : (partnerId, r.ArtistId);

                if (!edges.TryGetValue((lo, hi), out List<string>? titles))
                {
                    titles = [];
                    edges[(lo, hi)] = titles;
                }

                titles.Add(r.Title);
                usedNodes.Add(r.ArtistId);
                usedNodes.Add(partnerId);
            }
        }

        List<GraphNodeDto> nodes = usedNodes
            .Where(nodeById.ContainsKey)
            .Select(id => nodeById[id])
            .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<GraphEdgeDto> edgeDtos = edges
            .Select(e => new GraphEdgeDto(e.Key.Lo, e.Key.Hi, "split", string.Join(", ", e.Value.Distinct())))
            .ToList();

        return Ok(new GraphDto(nodes, edgeDtos));
    }
}
