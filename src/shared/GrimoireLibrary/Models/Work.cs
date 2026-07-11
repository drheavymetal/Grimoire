namespace Grimoire.Library.Models;

/// <summary>
/// A musical work (a composition), as opposed to a recording or a release. This is the
/// classical model (movement VII, D11): a symphony has a composer and many performances,
/// which the band/member model does not capture. The <c>works</c> table is populated by the
/// <c>classical</c> ETL verb from MusicBrainz work browses, one row per composition.
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
    /// destructive migration. Null when MusicBrainz gives the work no type — never invented.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// The composer of this work, as an <see cref="Artist"/> (a Person). This is how work↔composer
    /// is associated: the <c>works</c> table as shipped by the data-backbone migration carried no
    /// reference to an artist, so the classical ETL adds this single nullable foreign key (additive,
    /// non-destructive — DECISIONS/brief) to let a composer's page list their works. Null when the
    /// composer is unknown or fell outside the corpus. A work co-credited to several composers is
    /// attributed to the first that imports it (works.mbid is globally unique).
    /// </summary>
    public Guid? ComposerId { get; set; }

    public Artist? Composer { get; set; }
}
