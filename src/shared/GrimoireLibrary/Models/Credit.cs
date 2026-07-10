namespace Grimoire.Library.Models;

/// <summary>
/// A credit: an artist playing a role on a release or recording. Distinguishes
/// official members from guests. Model reserved; no table is created in this pass
/// (nothing writes credits yet).
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
}
