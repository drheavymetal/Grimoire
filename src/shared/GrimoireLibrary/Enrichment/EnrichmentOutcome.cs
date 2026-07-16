namespace Grimoire.Library.Enrichment;

/// <summary>
/// How one lookup against an external source resolved. The distinction that matters is between
/// the last two: <see cref="NoData"/> is the source answering "there is nothing here for this
/// artist", <see cref="Unavailable"/> is the source failing to answer at all.
/// <para>
/// Collapsing those two into a bare <c>null</c> is the bug this enum exists to prevent, and it bit
/// every enrichment pass in turn (MEMORY §6e, §6f). A pass records what it checked so a re-run
/// does not re-ask; if a timeout looks like a miss, that timeout is stamped <b>forever</b> as
/// "this band has no biography / is not on Metallum / is not on Last.fm", and no later run ever
/// revisits it. A miss is data. A failure is not — it is the absence of an answer, and the only
/// honest thing to do with it is ask again later (Invariant 5: a gap, never a guess).
/// </para>
/// </summary>
public enum EnrichmentOutcome
{
    /// <summary>The source had data for this artist, and it is in the result.</summary>
    Matched,

    /// <summary>
    /// The source answered definitively that it has nothing for this artist (a 404, an empty
    /// result set, an ambiguous match we refuse to guess at). A real gap — safe to stamp as
    /// checked, because asking again would only produce the same answer.
    /// </summary>
    NoData,

    /// <summary>
    /// The source could not answer: a timeout, a 429, a 5xx, a dropped connection. This says
    /// nothing about the artist, so the caller must <b>not</b> stamp it as checked — leave it
    /// unmarked and a later run will retry it.
    /// </summary>
    Unavailable,
}
