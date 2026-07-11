namespace Grimoire.Library.Models;

/// <summary>
/// A credit: an artist playing a role on a release or recording. Distinguishes
/// official members from guests, and records where the fact came from and how sure
/// we are of it (SPEC §10, D9). The table is created empty by the data-backbone
/// migration; the credits ETL populates it afterwards without a further migration.
/// </summary>
public class Credit
{
    public Guid Id { get; set; }

    public Guid ArtistId { get; set; }

    public Guid? ReleaseId { get; set; }

    public Guid? RecordingId { get; set; }

    /// <summary>performer | producer | engineer | mix | master</summary>
    public string Role { get; set; } = string.Empty;

    public string? Instrument { get; set; }

    public bool IsGuest { get; set; }

    /// <summary>
    /// Provenance of this credit: <c>discogs</c> | <c>musicbrainz</c> | <c>inferred</c>
    /// (SPEC §10, D9). An inferred credit — e.g. from interval intersection — must be
    /// shown as inferred in the UI, so the source is a first-class field, never invented.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// How much we trust this credit, in [0, 1]. Direct source facts sit near 1;
    /// inferred credits carry a lower confidence (D9). Stored as <c>real</c>.
    /// </summary>
    public float Confidence { get; set; }
}
