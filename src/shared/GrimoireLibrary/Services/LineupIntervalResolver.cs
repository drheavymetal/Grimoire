using Grimoire.Library.Models;

namespace Grimoire.Library.Services;

/// <summary>
/// Pure interval logic for the lineup timeline (features B7/B8). Given a set of
/// membership edges, resolves which members were active on a given date. Used to
/// light up the members that were in the band when a release came out.
/// </summary>
public static class LineupIntervalResolver
{
    /// <summary>
    /// Returns the membership edges active on <paramref name="date"/>. An edge is
    /// active when its interval contains the date, treating both ends as inclusive.
    /// A null <see cref="ArtistEdge.BeginDate"/> means an open start (always begun);
    /// a null <see cref="ArtistEdge.EndDate"/> means an open end (still active).
    /// Only <see cref="EdgeKind.MemberOf"/> edges are considered.
    /// </summary>
    public static IReadOnlyList<ArtistEdge> MembersActiveOn(IEnumerable<ArtistEdge> edges, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(edges);

        List<ArtistEdge> active = [];

        foreach (ArtistEdge edge in edges)
        {
            if (edge.Kind != EdgeKind.MemberOf)
            {
                continue;
            }

            bool startedByDate = edge.BeginDate is null || edge.BeginDate.Value <= date;
            bool notYetEnded = edge.EndDate is null || edge.EndDate.Value >= date;

            if (startedByDate && notYetEnded)
            {
                active.Add(edge);
            }
        }

        return active;
    }
}
