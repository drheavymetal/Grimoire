namespace Grimoire.Library.Models;

/// <summary>
/// One artist's Wikipedia biography in one language: the plain-text extract, the canonical URL of
/// the article it came from (the CC BY-SA attribution the licence requires must point at the
/// article the text is actually from, so it is stored per language and never derived), and when the
/// pass looked.
///
/// <para>
/// <b>Why a child table and not columns on <see cref="Artist"/>.</b> The band page must be readable
/// in the reader's language even when the only article is Norwegian, Swedish or Finnish — the
/// underground Grimoire exists to surface is largely Nordic and German, and it is exactly there that
/// English coverage collapses. A fixed <c>abstract_es</c>/<c>abstract_url_es</c> pair answers "es"
/// and nothing else: every further language would be another migration on the catalogue's hottest
/// table. A biography is a <em>collection</em> of (language, text, url), so it is modelled as one.
/// Adding <c>no</c>, <c>sv</c> or <c>fi</c> is configuration (<c>Wikipedia:Languages</c>), not schema.
/// </para>
/// <para>
/// <b>Why not jsonb on <c>artists</c>.</b> Two reasons, both learned here. (1) A value-converted
/// jsonb map cannot be filtered in SQL under this stack — the codebase already carries that scar
/// (<c>InfluenceJob</c> has to pull rows into memory to read a key out of <c>links</c>), and
/// "materialise 206k rows to find the pending ones" is the very bug D61 called out in
/// <c>ListenersJob</c>. An anti-join against this table stays in SQL. (2) Writing a biography would
/// mean UPDATEing an <c>artists</c> row, and those rows carry a 768-dimension embedding sitting in
/// an HNSW index: an UPDATE of that shape rewrites hundreds of megabytes and churns the index — the
/// failure mode that took production down (MEMORY §6f). INSERTs into this light table touch none of
/// that.
/// </para>
/// <para>
/// <b>English is deliberately absent.</b> It stays in <see cref="Artist.Abstract"/>/
/// <see cref="Artist.AbstractUrl"/>, not because English is special as a language but because that
/// text is what <c>EmbeddingTextBuilder</c> builds the vector from: moving it would change every
/// fingerprint (D62) and force a three-hour re-embed of the whole catalogue to gain nothing. Read
/// the two together through <c>Services.ArtistBiographies.Merge</c>, which is the only place that
/// asymmetry is allowed to show.
/// </para>
/// </summary>
public class ArtistBiography
{
    public Guid ArtistId { get; set; }

    /// <summary>
    /// The Wikipedia edition this biography came from, as a bare language code ("es", "no", "fi") —
    /// the leading label of the article host, so "no" means <c>no.wikipedia.org</c>. Half of the
    /// composite primary key with <see cref="ArtistId"/>.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// The plain-text extract, or null when this edition has no article for this artist. Null is a
    /// real, checked gap — the row's existence is the proof we asked (see <see cref="CheckedAt"/>) —
    /// never an invented biography (Invariant 5).
    /// </summary>
    public string? Abstract { get; set; }

    /// <summary>Canonical URL of the source article, for CC BY-SA attribution. Null when <see cref="Abstract"/> is.</summary>
    public string? AbstractUrl { get; set; }

    /// <summary>
    /// When the pass looked this artist up in this edition, matched or not. Never null: <b>the row
    /// itself is the marker</b>. A hit and a definitive miss both write a row; a transient failure
    /// (a WDQS timeout, a 429, a 5xx) writes <b>nothing</b>, so a later run retries it (D61). That is
    /// why the marker cannot be a column on <c>artists</c> shared with English: "already searched in
    /// Spanish" is a different fact from "already searched in English", and
    /// <c>abstract_checked_at</c> is already stamped on 206 882 rows — reusing it would mean no
    /// other language ever visited a single one of them.
    /// </summary>
    public DateTime CheckedAt { get; set; }
}
