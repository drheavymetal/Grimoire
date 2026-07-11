namespace Grimoire.Library.Models;

/// <summary>
/// A track: a recording placed at a position on a release. This is the row that unlocks
/// duration-as-an-axis (C7), song-title mining (C21) and the cover graph (C10). One row per
/// track of a release's representative medium set; a recording MBID may repeat across releases
/// (the same recording appearing on an album and later on a compilation), so <see cref="Mbid"/>
/// is deliberately NOT globally unique — the natural key is (<see cref="ReleaseId"/>,
/// <see cref="Position"/>).
/// </summary>
public class Recording
{
    public Guid Id { get; set; }

    /// <summary>MusicBrainz recording identifier. Not globally unique here: the same recording
    /// can be a track on several of our releases.</summary>
    public Guid Mbid { get; set; }

    public Guid ReleaseId { get; set; }

    public Release? Release { get; set; }

    /// <summary>The track title (release-specific track name preferred over the recording name).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Track length in milliseconds (MusicBrainz stores lengths in ms). Null when MusicBrainz
    /// has no length for the track or the recording — never invented (C7 degrades honestly).
    /// </summary>
    public int? LengthMs { get; set; }

    /// <summary>
    /// 1-based position of the track within the release, ordered across all its media
    /// (disc 1 track 1, …, disc 2 track 1, …). Unique per release.
    /// </summary>
    public int Position { get; set; }
}
