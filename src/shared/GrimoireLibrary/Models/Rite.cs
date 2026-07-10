namespace Grimoire.Library.Models;

/// <summary>
/// A blind tasting served to a user. The user's grimoire is not a table: it is the
/// set of rites in state <see cref="RiteState.Summoned"/>. Model reserved; no table
/// is created in this pass (nothing writes rites yet).
/// </summary>
public class Rite
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ArtistId { get; set; }

    public RiteState State { get; set; }

    public float Risk { get; set; }

    public DateTimeOffset ServedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
}
