namespace Grimoire.Library.Models;

/// <summary>
/// Nature of an <see cref="Artist"/>. Kept open so classical music (movement VII)
/// fits without a destructive migration.
/// </summary>
public enum ArtistKind
{
    Person,
    Group,
    Orchestra,
    Choir,
}

/// <summary>
/// Kind of relation between two artists in the bloodline graph.
/// </summary>
public enum EdgeKind
{
    MemberOf,
    SideProject,
    Collaboration,
    Teacher,
    Student,
    InfluencedBy,
}

/// <summary>
/// Type of a release. A demo is a first-class release in metal.
/// </summary>
public enum ReleaseType
{
    Album,
    Ep,
    Demo,
    Split,
    Live,
    Compilation,
}

/// <summary>
/// Inverse-popularity rarity tier, derived from listener count.
/// </summary>
public enum Rank
{
    Known,
    Obscure,
    Hidden,
    Forgotten,
    Nameless,
}

/// <summary>
/// State of a rite (blind tasting) for a given user and artist.
/// </summary>
public enum RiteState
{
    Served,
    Summoned,
    Banished,
    Again,
}
