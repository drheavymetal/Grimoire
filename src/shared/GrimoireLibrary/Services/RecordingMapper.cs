using Grimoire.Library.Models;

namespace Grimoire.Library.Services;

/// <summary>
/// Pure mapping from a MusicBrainz track/recording pair to a <see cref="Recording"/> row.
/// The bulk import itself is set-based SQL (DECISIONS D5: the MB mirror is a build artifact,
/// so a 30M-row recording table is distilled with joins, not row-by-row API calls). This
/// mapper is the reference implementation of the mapping rules the SQL mirrors, kept pure so
/// the contract — MBID matching, title/length fallback, position validity — is unit-tested.
///
/// Rules:
///  * the release-specific <c>track.name</c> wins over <c>recording.name</c>; a blank track
///    name falls back to the recording name;
///  * the release-specific <c>track.length</c> wins over <c>recording.length</c>; both are in
///    milliseconds, and an absent or non-positive length stays null (C7 degrades honestly,
///    never invents a duration);
///  * a track without a parseable recording MBID, without any title, or with a non-positive
///    position is refused (returns null) rather than mapped to a lie.
/// </summary>
public static class RecordingMapper
{
    /// <summary>
    /// Builds a <see cref="Recording"/> for <paramref name="releaseId"/> from a MusicBrainz
    /// track and its recording. Returns null when the recording MBID is not a GUID, no title
    /// is available, or the position is below 1.
    /// </summary>
    public static Recording? Map(
        string? recordingMbid,
        Guid releaseId,
        string? trackName,
        string? recordingName,
        int? trackLengthMs,
        int? recordingLengthMs,
        int position)
    {
        if (!Guid.TryParse(recordingMbid, out Guid mbid) || mbid == Guid.Empty)
        {
            return null;
        }

        if (position < 1)
        {
            return null;
        }

        string? title = FirstNonBlank(trackName, recordingName);

        if (title is null)
        {
            return null;
        }

        // Track length overrides recording length; a non-positive value is treated as absent.
        int? length = trackLengthMs ?? recordingLengthMs;

        if (length is <= 0)
        {
            length = null;
        }

        return new Recording
        {
            Id = Guid.NewGuid(),
            Mbid = mbid,
            ReleaseId = releaseId,
            Title = title,
            LengthMs = length,
            Position = position,
        };
    }

    /// <summary>
    /// Validates a cover/version edge between two recordings. Returns the trimmed relation name
    /// when both MBIDs parse and differ, otherwise null — an edge from a recording to itself, or
    /// with an unparseable endpoint, is not a version relation.
    /// </summary>
    public static string? MapCoverRelation(string? originalMbid, string? coverMbid, string? relation)
    {
        if (!Guid.TryParse(originalMbid, out Guid original) || original == Guid.Empty)
        {
            return null;
        }

        if (!Guid.TryParse(coverMbid, out Guid cover) || cover == Guid.Empty)
        {
            return null;
        }

        if (original == cover)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(relation) ? null : relation.Trim();
    }

    private static string? FirstNonBlank(string? first, string? second)
    {
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first.Trim();
        }

        if (!string.IsNullOrWhiteSpace(second))
        {
            return second.Trim();
        }

        return null;
    }
}
