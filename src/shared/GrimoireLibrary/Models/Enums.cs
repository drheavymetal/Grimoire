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

/// <summary>
/// Kind of in-app notification dropped into a user's inbox (the NOTIFICATIONS wave). A
/// <see cref="FriendRequest"/> lands when someone asks to be your friend; a
/// <see cref="FriendAccepted"/> when a request you sent is accepted (or completed as a mutual
/// accept); a <see cref="GiftReceived"/> when a friend sends you a band face-down (C22); a
/// <see cref="RaritySurpassed"/> when a friend's summon pushes their Depth Score above yours (they
/// just went deeper than you); a <see cref="DuelChallenge"/> when a friend invites you to a taste
/// face-off; a <see cref="VerdictGamePlayed"/> when a friend finished guessing which of your bands
/// you summoned and which you banished (the GAMES wave) — it carries their score and is the
/// invitation to play back, which is what makes that game turn-based with no realtime at all.
/// Stored as a string, like the other enums. The type also decides which payload fields are present.
/// </summary>
public enum NotificationType
{
    FriendRequest,
    FriendAccepted,
    GiftReceived,
    RaritySurpassed,
    DuelChallenge,
    VerdictGamePlayed,
}

/// <summary>
/// Which game a <see cref="Game"/> row is (the GAMES wave). <see cref="Verdict"/> is "did your
/// friend summon this band, or banish it?" — 45 blind seconds from their resolved rites, scored on
/// how well you know their ear. Stored as a string, like the other enums, so the second game (guess
/// the band) is a new member and not a migration. The discriminator exists from the first row on
/// purpose: adding it later would mean backfilling every row that already shipped.
/// </summary>
public enum GameKind
{
    Verdict,
}

/// <summary>
/// Lifecycle of a <see cref="Game"/> (the GAMES wave). A game is dealt whole and starts
/// <see cref="InProgress"/>; answering the last round makes it <see cref="Finished"/> and notifies
/// the opponent. There is no "abandoned": an unfinished game is simply resumable, because its rounds
/// were already dealt and nothing about them expires.
/// </summary>
public enum GameStatus
{
    InProgress,
    Finished,
}

/// <summary>
/// How hard a <see cref="Game"/> was dealt, for the kinds that have difficulties (the second game,
/// guess the band, has two). Modelled now so the column exists before the rows do; the verdict game
/// leaves it null.
/// </summary>
public enum GameDifficulty
{
    Normal,
    Hard,
}

/// <summary>
/// State of a <see cref="Friendship"/> between two users (the FRIENDS wave). A row is created
/// <see cref="Pending"/> by the requester, moves to <see cref="Accepted"/> when the addressee
/// agrees, and <see cref="Blocked"/> is a one-directional wall the requester raises against the
/// addressee (a blocked user cannot send friend requests). Stored as a string, like the other enums.
/// </summary>
public enum FriendshipStatus
{
    Pending,
    Accepted,
    Blocked,
}
