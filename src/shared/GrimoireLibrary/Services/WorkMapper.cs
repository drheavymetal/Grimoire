using Grimoire.Library.Models;

namespace Grimoire.Library.Services;

/// <summary>
/// Pure mapping from a MusicBrainz work (as browsed by composer) to a <see cref="Work"/> row,
/// independent of the JSON DTOs so it can be unit-tested directly. Refuses to build a row from
/// an unparseable MBID or an empty title, and leaves an absent work type as null rather than
/// inventing one (movement VII, D11).
/// </summary>
public static class WorkMapper
{
    /// <summary>
    /// Builds a <see cref="Work"/> for <paramref name="composerId"/> from a MusicBrainz work's raw
    /// id, title and type. Returns null when the id is not a GUID or the title is blank.
    /// </summary>
    public static Work? Map(string? mbid, string? title, string? type, Guid composerId)
    {
        if (!Guid.TryParse(mbid, out Guid workMbid) || workMbid == Guid.Empty)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new Work
        {
            Id = Guid.NewGuid(),
            Mbid = workMbid,
            Title = title.Trim(),
            Kind = string.IsNullOrWhiteSpace(type) ? null : type.Trim(),
            ComposerId = composerId,
        };
    }
}
