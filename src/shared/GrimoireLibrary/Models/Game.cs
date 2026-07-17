namespace Grimoire.Library.Models;

/// <summary>
/// One played game (the GAMES wave). A game is a fixed deal of blind rounds a single
/// <see cref="PlayerId"/> answers, scored against real data — never against an invented truth.
/// <see cref="Kind"/> discriminates which game it is, so a second game (guess the band) reuses this
/// table rather than forcing a discriminator onto rows that already exist.
///
/// The match is ONE-DIRECTIONAL on purpose: a game is "this player, guessing about that opponent",
/// self-contained and always valid. Turn-taking is the inbox's job — finishing a game drops a
/// notification on the opponent, who replies by starting their own game back (DECISIONS D60: the
/// mailbox is the turn structure; nothing here is realtime and nothing waits on the other side).
/// </summary>
public class Game
{
    public Guid Id { get; set; }

    /// <summary>Which game this is. Stored as a string, like the other enums — a new kind is not a migration.</summary>
    public GameKind Kind { get; set; }

    /// <summary>Whoever plays and answers the rounds.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>
    /// The other side of the match, or null for a game played against nobody — the solo mode, which
    /// fell out of a nullable column rather than a table, exactly as intended.
    ///
    /// <para>
    /// What "the other side" means is the kind's business, and the two kinds differ:
    /// <see cref="GameKind.Verdict"/> reads the opponent's grimoire (they ARE the subject — the rounds
    /// are their verdicts), while <see cref="GameKind.GuessBand"/> never touches it. There, both
    /// players play their OWN summons and only the scores meet (D67): the column names who the score
    /// is sent to, not whose data was read. That is why guess-the-band needs no consent gate — nothing
    /// of the opponent's is exposed by it, which is the entire reason D66 needed one.
    /// </para>
    /// </summary>
    public Guid? OpponentId { get; set; }

    /// <summary>
    /// The difficulty this game was dealt at, when its kind has difficulties. Null when the kind has
    /// none — the verdict game does not: "summoned or banished" is one binary question, and a
    /// difficulty knob over a coin flip would be decoration. Guess-the-band sets it, and it is
    /// SNAPSHOT here for the same reason <see cref="GameRound.Truth"/> is: it decides what a round is
    /// worth, so re-reading it from anywhere else would let a score move under a finished game.
    /// </summary>
    public GameDifficulty? Difficulty { get; set; }

    public GameStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the last round was answered. Null while the game is still in progress.</summary>
    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>
/// One blind round of a <see cref="Game"/> (the GAMES wave). The row's <see cref="Id"/> doubles as
/// the capability token for the audio proxy (<c>GET /api/games/rounds/{token}/audio</c>), exactly as
/// <see cref="Rite.Id"/> does for The Rite (DECISIONS D32): a random GUID the client holds instead of
/// the preview's origin URL, so devtools never sees where the audio comes from and the round stays
/// blind.
///
/// <see cref="Truth"/> is SNAPSHOT at deal time rather than read back from <c>rites</c> at scoring
/// time. Two reasons: the score can never drift under a player mid-game, and the round stays a
/// self-contained record of what was actually asked.
/// </summary>
public class GameRound
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }

    /// <summary>Position in the deal, 0-based — the order the rounds are played and displayed in.</summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// The band being played, blind. NEVER leaves the server until the round is answered.
    ///
    /// <para>
    /// In <see cref="GameKind.GuessBand"/> this id IS the answer, so it leaks the round outright — and
    /// there it leaks even harder than in the verdict game, where the id merely lets you look the band
    /// up in your friend's public grimoire. The multiple-choice mode is the one place it goes out at
    /// all, and only ever shuffled in among three decoys that are shaped identically (D67).
    /// </para>
    /// </summary>
    public Guid ArtistId { get; set; }

    /// <summary>
    /// The correct answer, snapshot when the round was dealt: the opponent's verdict on this band
    /// (<see cref="RiteState.Summoned"/> or <see cref="RiteState.Banished"/>) for the verdict game.
    /// Null for a kind whose truth is the band itself (guess the band), where the answer is
    /// <see cref="ArtistId"/> and a copy would be a second source of the same fact.
    /// </summary>
    public RiteState? Truth { get; set; }

    /// <summary>
    /// What the player answered. Null until they do — and the round's DTO stays blind while it is.
    ///
    /// <para>
    /// This column is the verdict game's, and only its: a verdict is one of two words and fits. A
    /// guess-the-band answer is a band — an id or a typed name — and neither fits in sixteen
    /// characters, so that kind leaves this null and keeps only <see cref="Correct"/>, which is
    /// decided server-side at answer time against the real name and is what the score is built from.
    /// The cost is real and small: a reviewed round can say what the band was and whether you got it,
    /// but not what you typed. Truncating a name to fit here would store a different fact under this
    /// field's name, and a lie in a column is worse than a gap (Invariant 5).
    /// </para>
    /// </summary>
    public RiteState? Answer { get; set; }

    /// <summary>
    /// Whether the player got the round right. For the verdict game, whether <see cref="Answer"/>
    /// matched <see cref="Truth"/>; for guess-the-band, whether the name they gave resolved to
    /// <see cref="ArtistId"/> (see <c>GuessMatch</c>). Null until answered.
    /// </summary>
    public bool? Correct { get; set; }

    public DateTimeOffset? AnsweredAt { get; set; }
}
