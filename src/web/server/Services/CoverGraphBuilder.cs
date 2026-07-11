namespace Grimoire.Server.Services;

/// <summary>
/// Pure shaping for the version graph (C10, "quién versionó a quién"). The <c>cover_versions</c>
/// family is dominated by an artist's own remixes/remasters of themselves; the honest "who covered
/// <b>someone else</b>" subset is the <b>cross-artist</b> slice (SPEC §5.8, the import contract).
/// This filter — <c>original.artist ≠ cover.artist</c> — is the one load-bearing rule, so it lives
/// here database-free and the test bites it directly.
/// </summary>
public static class CoverGraphBuilder
{
    /// <summary>
    /// One cover relation between two recordings, carrying the artist on each end (derived through
    /// the release), the MusicBrainz relation name, and the covered song's title.
    /// </summary>
    public readonly record struct RawCover(
        Guid OriginalArtistId,
        Guid CoverArtistId,
        string Relation,
        string Title);

    /// <summary>
    /// Keeps only the cross-artist covers — where the covering artist is not the original artist.
    /// Drops an artist's versions of their own recordings (own remixes/remasters/edits), which are
    /// real relations but not the "someone else covered this" story the graph tells.
    /// </summary>
    public static IReadOnlyList<RawCover> CrossArtist(IEnumerable<RawCover> rows)
    {
        return rows.Where(r => r.OriginalArtistId != r.CoverArtistId).ToList();
    }
}
