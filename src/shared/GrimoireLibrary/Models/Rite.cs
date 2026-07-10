namespace Grimoire.Library.Models;

/// <summary>
/// A blind tasting served to a user. The user's grimoire is not a table: it is the
/// set of rites in state <see cref="RiteState.Summoned"/>. The row's <see cref="Id"/> doubles
/// as the capability token for the audio proxy (<c>GET /api/rite/{token}/audio</c>): a random
/// GUID the client holds instead of the preview's origin URL, so devtools never sees where the
/// audio comes from and the mechanic stays blind (SPEC §5.3).
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
