namespace Grimoire.Library.Models;

/// <summary>
/// A musical work (a composition), as opposed to a recording or a release. Reserved for
/// classical music (movement VII, D11): a symphony has a composer and many performances,
/// which the band/member model does not capture. The table is created minimal but real by
/// the data-backbone migration so the schema exists; nothing populates it in this movement.
/// </summary>
public class Work
{
    public Guid Id { get; set; }

    /// <summary>MusicBrainz work identifier. Unique when present.</summary>
    public Guid Mbid { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Kind of work as MusicBrainz classifies it (e.g. symphony, opera, song). Free text,
    /// kept open like <see cref="ArtistKind"/> so the classical model can grow without a
    /// destructive migration.
    /// </summary>
    public string? Kind { get; set; }
}
