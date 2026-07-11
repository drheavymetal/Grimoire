using Pgvector;

namespace Grimoire.Library.Models;

/// <summary>
/// A musical artist: a person, group, orchestra or choir.
/// </summary>
public class Artist
{
    public Guid Id { get; set; }

    /// <summary>MusicBrainz identifier. Unique when present.</summary>
    public Guid Mbid { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? SortName { get; set; }

    public ArtistKind Kind { get; set; }

    /// <summary>ISO country code, when known.</summary>
    public string? Country { get; set; }

    public string? City { get; set; }

    public int? FormedYear { get; set; }

    public int? DissolvedYear { get; set; }

    /// <summary>
    /// Date of death (feature C12, In Memoriam). Populated only for people Wikidata asserts
    /// have died (P570). Null means "no death on record" — never invented.
    /// </summary>
    public DateOnly? DeathDate { get; set; }

    /// <summary>Place of death (Wikidata P20), when asserted. Null otherwise.</summary>
    public string? DeathPlace { get; set; }

    /// <summary>Last.fm listener count. Null until the Last.fm enrichment pass runs.</summary>
    public int? Listeners { get; set; }

    public string[] Tags { get; set; } = [];

    public string? Abstract { get; set; }

    /// <summary>Text embedding (nomic-embed-text, 768 dims). Null until the embedding pass runs.</summary>
    public Vector? Embedding { get; set; }

    /// <summary>Rarity tier derived from <see cref="Listeners"/>. Null while listeners are unknown.</summary>
    public Rank? Rank { get; set; }

    /// <summary>
    /// X coordinate of the 2D projection of <see cref="Embedding"/>, for the Atlas (C18/B22).
    /// SPEC §10 sketches this as an <c>xy point</c>; it is stored instead as two plain
    /// <c>double precision</c> columns (<see cref="XyX"/>/<see cref="XyY"/>) because Npgsql's
    /// <c>NpgsqlPoint</c> is a struct whose nullability and value-comparison are awkward under
    /// EF, whereas two nullable doubles are trivial, null-safe (both null = not yet projected),
    /// and the front reads the pair directly. Null until the <c>atlas</c> pass runs.
    /// </summary>
    public double? XyX { get; set; }

    /// <summary>Y coordinate of the 2D projection. See <see cref="XyX"/>.</summary>
    public double? XyY { get; set; }

    /// <summary>Streaming and external links, keyed by service. Stored as jsonb.</summary>
    public Dictionary<string, string>? Links { get; set; }

    /// <summary>
    /// A 30–45 s audio preview URL (iTunes first, Deezer as complement — DECISIONS D25).
    /// Null when neither source has audio for this artist: roughly half the underground is
    /// genuinely inaudible, so null is a real gap, never invented. The Rite pool is filtered
    /// on <c>preview_url IS NOT NULL</c>.
    /// </summary>
    public string? PreviewUrl { get; set; }

    public string? ImageUrl { get; set; }

    public List<Release> Releases { get; set; } = [];
}
