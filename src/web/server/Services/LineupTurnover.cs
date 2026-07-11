using Grimoire.Library.Models;
using Grimoire.Library.Services;

namespace Grimoire.Server.Services;

/// <summary>
/// "The disc where everything changed" (feature B12): the release around whose date the band's
/// lineup churned the most. Reuses <see cref="LineupIntervalResolver"/> — the same interval
/// intersection that lights up the Gantt (B7/B8) — to read the active lineup a window before and
/// a window after each release, and counts who joined and who left across that gap.
///
/// <para>
/// Pure and database-free so the turnover count is unit-tested directly. Nothing is invented: a
/// release with no date cannot be placed on the timeline and is skipped; a band whose best
/// release sees zero churn yields no pivotal release (an honest "nothing changed", not a lie).
/// </para>
/// </summary>
public static class LineupTurnover
{
    /// <summary>
    /// Half-window, in days, on each side of a release date. A membership counted as "joined" or
    /// "left" around a release is one whose active state differs between one year before and one
    /// year after the release — a lineup change in that record's neighbourhood.
    /// </summary>
    public const int WindowDays = 365;

    /// <summary>The turnover around one release: who came in and who went out near its date.</summary>
    public sealed record ReleaseTurnover(
        Guid ReleaseId,
        int Score,
        IReadOnlyList<Guid> Joined,
        IReadOnlyList<Guid> Left);

    /// <summary>
    /// The single release with the greatest lineup turnover for a band, or null when no dated
    /// release sees any change. <paramref name="viewedArtistId"/> is the band being viewed, used
    /// to read each membership's counterpart (the member). Ties go to the earliest release.
    /// </summary>
    public static ReleaseTurnover? MostPivotal(
        Guid viewedArtistId,
        IReadOnlyList<(Guid Id, DateOnly Date)> releases,
        IReadOnlyList<ArtistEdge> edges,
        int windowDays = WindowDays)
    {
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(edges);

        ReleaseTurnover? best = null;
        DateOnly bestDate = DateOnly.MaxValue;

        foreach ((Guid releaseId, DateOnly date) in releases)
        {
            ReleaseTurnover turnover = Around(viewedArtistId, releaseId, date, edges, windowDays);

            if (turnover.Score == 0)
            {
                continue;
            }

            // Higher score wins; on a tie the earlier release is the truer "moment it changed".
            if (best is null || turnover.Score > best.Score || (turnover.Score == best.Score && date < bestDate))
            {
                best = turnover;
                bestDate = date;
            }
        }

        return best;
    }

    /// <summary>
    /// The turnover around a single release: members active a window after but not before joined;
    /// members active before but not after left. Score is the total of both.
    /// </summary>
    public static ReleaseTurnover Around(
        Guid viewedArtistId,
        Guid releaseId,
        DateOnly date,
        IReadOnlyList<ArtistEdge> edges,
        int windowDays = WindowDays)
    {
        ArgumentNullException.ThrowIfNull(edges);

        DateOnly before = date.AddDays(-windowDays);
        DateOnly after = date.AddDays(windowDays);

        HashSet<Guid> activeBefore = MemberIdsActiveOn(viewedArtistId, edges, before);
        HashSet<Guid> activeAfter = MemberIdsActiveOn(viewedArtistId, edges, after);

        List<Guid> joined = activeAfter.Where(id => !activeBefore.Contains(id)).OrderBy(id => id).ToList();
        List<Guid> left = activeBefore.Where(id => !activeAfter.Contains(id)).OrderBy(id => id).ToList();

        return new ReleaseTurnover(releaseId, joined.Count + left.Count, joined, left);
    }

    /// <summary>
    /// The member ids active on a date, resolved via <see cref="LineupIntervalResolver"/> and
    /// mapped to the counterpart of the viewed artist (the member, when viewing a band).
    /// </summary>
    private static HashSet<Guid> MemberIdsActiveOn(Guid viewedArtistId, IReadOnlyList<ArtistEdge> edges, DateOnly date)
    {
        IReadOnlyList<ArtistEdge> active = LineupIntervalResolver.MembersActiveOn(edges, date);

        var ids = new HashSet<Guid>();
        foreach (ArtistEdge edge in active)
        {
            Guid memberId = edge.FromId == viewedArtistId ? edge.ToId : edge.FromId;
            ids.Add(memberId);
        }

        return ids;
    }
}
