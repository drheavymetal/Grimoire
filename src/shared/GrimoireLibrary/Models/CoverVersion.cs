namespace Grimoire.Library.Models;

/// <summary>
/// A directed "version of" edge between two recordings, feeding the version graph (C10,
/// "quién versionó a quién"). MusicBrainz has NO atomic recording→recording "cover" link:
/// true cover attribution lives at the <c>work</c> level (the reserved classical model,
/// movement VII, D11). What MusicBrainz does expose at the recording level is the
/// "covers and versions" family of <c>l_recording_recording</c> relations — other versions,
/// edit, remaster, a cappella, instrumental, karaoke, remix, mash-up — so that family is the
/// honest v1 signal for the version graph. Each edge keeps its <see cref="Relation"/> (the MB
/// relationship name) so the UI can label it rather than pretend every edge is a cover.
///
/// The endpoints are recordings; the artists on either side are derived through
/// <see cref="Recording.Release"/> → release artist. An edge is only stored when BOTH
/// recordings are in our imported set, so neither foreign key ever dangles.
/// </summary>
public class CoverVersion
{
    public Guid Id { get; set; }

    /// <summary>The earlier / source recording (MusicBrainz <c>entity0</c>).</summary>
    public Guid OriginalRecordingId { get; set; }

    public Recording? Original { get; set; }

    /// <summary>The derivative recording — the version / cover (MusicBrainz <c>entity1</c>).</summary>
    public Guid CoverRecordingId { get; set; }

    public Recording? Cover { get; set; }

    /// <summary>
    /// The MusicBrainz relationship name for this edge (e.g. <c>other versions</c>, <c>remix</c>,
    /// <c>remaster</c>). Kept verbatim so the graph can show what kind of version it is.
    /// </summary>
    public string Relation { get; set; } = string.Empty;
}
