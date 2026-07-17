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
/// invitation to play back, which is what makes that game turn-based with no realtime at all; a
/// <see cref="GuessGamePlayed"/> when a friend named their own summons blind (D67) and sent you the
/// score to beat — the same turn hand-off, over a game where each side plays their own grimoire.
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
    GuessGamePlayed,
}

/// <summary>
/// Which game a <see cref="Game"/> row is (the GAMES wave). <see cref="Verdict"/> is "did your
/// friend summon this band, or banish it?" — 45 blind seconds from their resolved rites, scored on
/// how well you know their ear. Stored as a string, like the other enums, so the second game (guess
/// the band) is a new member and not a migration. The discriminator exists from the first row on
/// purpose: adding it later would mean backfilling every row that already shipped.
///
/// <para>
/// <see cref="GuessBand"/> is that second game, and it arrived exactly as the column promised: a new
/// enum member, no migration, no backfill (D67). It is "you loved it blind — do you even know who it
/// is?", played over the player's OWN summons. A general name quiz was refused twice (D43, D66):
/// naming rewards whoever already knows the canon, which inverts the Ranks pillar the whole app is
/// built to argue with. Bounded to your own grimoire it asks the opposite question, and the joke lands.
/// </para>
/// </summary>
public enum GameKind
{
    Verdict,
    GuessBand,
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
/// How hard a <see cref="Game"/> was dealt, for the kinds that have difficulties. The verdict game
/// leaves it null — "summoned or banished" is one binary question and a difficulty knob over a coin
/// flip would be decoration. Guess-the-band has both (D67), and the column was modelled before its
/// first row existed so that adding them cost nothing.
///
/// <para>
/// <see cref="Normal"/> shows four names to choose from, the three wrong ones drawn from the player's
/// own grimoire and as close to the answer in the embedding map as it has (D26/D31). Near decoys are
/// the whole difficulty: random ones would make the round a formality.
/// </para>
/// <para>
/// <see cref="Hard"/> shows nothing and the player types the name, which is worth more because the
/// baseline collapses — one in four is free on a multiple choice, and nothing is free on a blank
/// field. See <c>GuessGamePool.PointsPerRound</c>.
/// </para>
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
