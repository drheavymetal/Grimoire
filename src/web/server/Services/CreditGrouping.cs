using Grimoire.Library.Models;
using Grimoire.Server.Dtos;

namespace Grimoire.Server.Services;

/// <summary>
/// Pure grouping for per-release credits (feature B9). Turns the flat <c>credits</c> rows —
/// one row per (artist, release, recording, role, instrument) — into a per-release shape the
/// artist page can render: who performed (official member vs guest, with their instruments)
/// and who produced, engineered, mixed or mastered.
///
/// <para>
/// Kept database-free so the two decisions that matter — the member-vs-guest split and the
/// performer/production split — are unit-tested directly (D9: "miembro oficial ≠ invitado").
/// Nothing is invented here: an artist with no non-guest performer row on a release is a
/// guest, and a release the ETL never reached simply has no rows and yields no entry.
/// </para>
/// </summary>
public static class CreditGrouping
{
    /// <summary>The roles that count as production, in the order they are shown.</summary>
    private static readonly string[] ProductionOrder = ["producer", "engineer", "mix", "master"];

    /// <summary>The performer role — everything else that is not production.</summary>
    private const string PerformerRole = "performer";

    /// <summary>
    /// One flat credit row, carrying the performer's display name so the grouping needs no
    /// second lookup. <paramref name="Instrument"/> is null for a production credit.
    /// </summary>
    public sealed record CreditRow(
        Guid ReleaseId,
        Guid ArtistId,
        string ArtistName,
        Rank? ArtistRank,
        string Role,
        string? Instrument,
        bool IsGuest);

    /// <summary>
    /// Groups the rows by release. Returns one <see cref="ReleaseCreditsDto"/> per release that
    /// has at least one credit, releases in ascending id order for a stable shape (the caller
    /// keys by release id, so order is only for determinism).
    /// </summary>
    public static IReadOnlyList<ReleaseCreditsDto> Group(IEnumerable<CreditRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var byRelease = new Dictionary<Guid, List<CreditRow>>();
        foreach (CreditRow row in rows)
        {
            if (!byRelease.TryGetValue(row.ReleaseId, out List<CreditRow>? list))
            {
                list = [];
                byRelease[row.ReleaseId] = list;
            }

            list.Add(row);
        }

        List<ReleaseCreditsDto> result = [];
        foreach ((Guid releaseId, List<CreditRow> releaseRows) in byRelease)
        {
            result.Add(GroupOne(releaseId, releaseRows));
        }

        return result
            .OrderBy(r => r.ReleaseId)
            .ToList();
    }

    private static ReleaseCreditsDto GroupOne(Guid releaseId, List<CreditRow> rows)
    {
        // --- Performers: one entry per artist, collecting their instruments across recordings. ---
        List<PerformerCreditDto> performers = rows
            .Where(r => IsPerformer(r.Role))
            .GroupBy(r => r.ArtistId)
            .Select(g =>
            {
                CreditRow first = g.First();

                List<string> instruments = g
                    .Select(r => r.Instrument)
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .Select(i => i!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(i => i, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // A guest only if EVERY performer row for this artist on this release is a guest:
                // one official credit makes them a member (D9 — the distinction is load-bearing).
                bool isGuest = g.All(r => r.IsGuest);

                return new PerformerCreditDto(first.ArtistId, first.ArtistName, first.ArtistRank, instruments, isGuest);
            })
            // Official members before guests; then by name.
            .OrderBy(p => p.IsGuest)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // --- Production: one entry per (artist, role), ordered by role then name. ---
        List<ProductionCreditDto> production = rows
            .Where(r => !IsPerformer(r.Role))
            .GroupBy(r => new { r.ArtistId, Role = NormaliseRole(r.Role) })
            .Select(g =>
            {
                CreditRow first = g.First();
                return new ProductionCreditDto(first.ArtistId, first.ArtistName, g.Key.Role);
            })
            .OrderBy(p => ProductionRank(p.Role))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ReleaseCreditsDto(releaseId, performers, production);
    }

    private static bool IsPerformer(string role)
    {
        return string.Equals(NormaliseRole(role), PerformerRole, StringComparison.Ordinal);
    }

    private static string NormaliseRole(string role)
    {
        return (role ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static int ProductionRank(string role)
    {
        int index = Array.IndexOf(ProductionOrder, role);
        return index < 0 ? ProductionOrder.Length : index;
    }
}
