namespace Grimoire.Library.Models;

/// <summary>
/// A directed relation between two artists (membership, side project, collaboration,
/// teacher/student, influence). Membership edges carry dates and instruments and
/// feed the lineup timeline.
/// </summary>
public class ArtistEdge
{
    public Guid Id { get; set; }

    public Guid FromId { get; set; }

    public Artist? From { get; set; }

    public Guid ToId { get; set; }

    public Artist? To { get; set; }

    public EdgeKind Kind { get; set; }

    public DateOnly? BeginDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string[] Instruments { get; set; } = [];
}
