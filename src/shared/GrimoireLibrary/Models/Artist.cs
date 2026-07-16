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

    /// <summary>
    /// When the Last.fm pass last looked this artist up, matched or not. The resume marker, and the
    /// reason a null <see cref="Listeners"/> is no longer ambiguous: without it the pass re-asked
    /// Last.fm for every artist it had already failed to find, forever — the ~2 800 genuine misses
    /// were re-crawled every twenty minutes for nothing (MEMORY §6f). A non-null stamp means
    /// "already asked, do not ask again"; it is only ever written on a definitive answer, so a 429
    /// or a timeout leaves it null and a later run retries.
    /// </summary>
    public DateTime? ListenersCheckedAt { get; set; }

    public string[] Tags { get; set; } = [];

    public string? Abstract { get; set; }

    /// <summary>
    /// Source URL of the <see cref="Abstract"/> (the English Wikipedia article), kept for the
    /// CC BY-SA attribution the licence requires. Null when no biography was matched. Populated
    /// alongside <see cref="Abstract"/> by the Wikipedia pass.
    /// </summary>
    public string? AbstractUrl { get; set; }

    /// <summary>
    /// When the Wikipedia biography pass last looked this artist up, matched or not. The resume
    /// marker: a non-null value means "already checked, do not fetch again" so a re-run never
    /// re-queries an artist Wikidata/Wikipedia has no article for (a gap, never a guess).
    /// </summary>
    public DateTime? AbstractCheckedAt { get; set; }

    /// <summary>Text embedding (nomic-embed-text, 768 dims). Null until the embedding pass runs.</summary>
    public Vector? Embedding { get; set; }

    /// <summary>
    /// Fingerprint of the text <see cref="Embedding"/> was built from (see
    /// <c>EmbeddingTextBuilder.Fingerprint</c>). It answers the question the embedding pass could
    /// not otherwise ask: <em>is this vector still true?</em> Enrichment keeps rewriting the source
    /// text — a band gains Last.fm tags, a Wikipedia biography, another member — and each time, the
    /// stored vector silently describes a band we no longer have. Comparing this against the text's
    /// current fingerprint re-embeds exactly what changed and nothing else. Null means "never
    /// embedded, or embedded before this column existed", both of which want re-embedding.
    /// </summary>
    public string? EmbeddingFingerprint { get; set; }

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

    /// <summary>
    /// Metal Archives band id, when this band was matched on Metallum (D48). Drives the Metallum
    /// link every band page owes them (Invariant 3) and marks the row as matched. Null = not on
    /// MA, or matched ambiguously and left alone (name+country+year, never guessed — D48/R3).
    /// </summary>
    public int? MetalArchivesId { get; set; }

    /// <summary>Metal Archives' own genre string (e.g. "Black Metal (early); Ambient (later)"). Null until matched.</summary>
    public string? MetalArchivesGenre { get; set; }

    /// <summary>
    /// Lyrical themes from Metal Archives — the one field that exists nowhere else (D48/Q4). Empty
    /// until the MA pass matches the band; empty is a real gap, never invented (Invariant 5).
    /// </summary>
    public string[] LyricalThemes { get; set; } = [];

    /// <summary>
    /// When the Metal Archives pass last looked this band up, matched or not. The resume marker: a
    /// non-null value means "already checked, do not fetch again" so a re-run never re-crawls a band
    /// MA has no entry for (honours the one-pass, don't-hammer terms of D42/D48).
    /// </summary>
    public DateTime? MetalArchivesCheckedAt { get; set; }

    public List<Release> Releases { get; set; } = [];
}
