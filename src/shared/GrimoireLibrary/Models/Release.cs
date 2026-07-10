namespace Grimoire.Library.Models;

/// <summary>
/// A release by an artist. A demo is a first-class release, not hidden under a toggle.
/// </summary>
public class Release
{
    public Guid Id { get; set; }

    /// <summary>MusicBrainz release-group identifier. Unique when present.</summary>
    public Guid Mbid { get; set; }

    public Guid ArtistId { get; set; }

    public Artist? Artist { get; set; }

    public string Title { get; set; } = string.Empty;

    public ReleaseType Type { get; set; }

    public DateOnly? ReleaseDate { get; set; }

    public Guid? LabelId { get; set; }

    public Label? Label { get; set; }

    public string? CoverUrl { get; set; }
}
