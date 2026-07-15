using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Services;

/// <summary>
/// Crosses two grimoires (feature C23): what the other user has summoned that you have not, the
/// reverse, and the common ground. Extracted so both the by-code path (<c>RiteController</c>) and the
/// named-friend path (<c>FriendsController</c>) share exactly one implementation. Nothing is invented:
/// an empty grimoire on either side simply yields empty lists.
/// </summary>
public sealed class GrimoireCrossService
{
    private readonly GrimoireDbContext _db;

    public GrimoireCrossService(GrimoireDbContext db)
    {
        _db = db;
    }

    /// <summary>Crosses <paramref name="mine"/>'s grimoire against <paramref name="other"/>'s.</summary>
    public async Task<CrossedGrimoiresDto> CrossAsync(Guid mine, Guid other, CancellationToken ct)
    {
        HashSet<Guid> mineIds = (await _db.Rites
                .Where(r => r.UserId == mine && r.State == RiteState.Summoned)
                .Select(r => r.ArtistId)
                .ToListAsync(ct))
            .ToHashSet();

        HashSet<Guid> theirs = (await _db.Rites
                .Where(r => r.UserId == other && r.State == RiteState.Summoned)
                .Select(r => r.ArtistId)
                .ToListAsync(ct))
            .ToHashSet();

        List<Guid> theirsOnlyIds = theirs.Where(id => !mineIds.Contains(id)).ToList();
        List<Guid> yoursOnlyIds = mineIds.Where(id => !theirs.Contains(id)).ToList();
        List<Guid> sharedIds = mineIds.Where(theirs.Contains).ToList();

        Dictionary<Guid, ArtistSummaryDto> summaries = await SummariesAsync(
            theirsOnlyIds.Concat(yoursOnlyIds).Concat(sharedIds).ToHashSet(), ct);

        return new CrossedGrimoiresDto(
            Order(theirsOnlyIds, summaries),
            Order(yoursOnlyIds, summaries),
            Order(sharedIds, summaries));
    }

    private async Task<Dictionary<Guid, ArtistSummaryDto>> SummariesAsync(IReadOnlySet<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Artists
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new ArtistSummaryDto(a.Id, a.Name, a.Country, a.FormedYear, a.Rank))
            .ToDictionaryAsync(a => a.Id, ct);
    }

    private static List<ArtistSummaryDto> Order(IEnumerable<Guid> ids, IReadOnlyDictionary<Guid, ArtistSummaryDto> summaries)
    {
        return ids
            .Select(id => summaries.TryGetValue(id, out ArtistSummaryDto? s) ? s : null)
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
