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
    /// The friend whose verdicts are being guessed. Null for a game played against nobody — the
    /// solo mode the second game needs, which falls out of a nullable column rather than a table.
    /// </summary>
    public Guid? OpponentId { get; set; }

    /// <summary>
    /// The difficulty this game was dealt at, when its kind has difficulties. Null when the kind has
    /// none — the verdict game does not: "summoned or banished" is one binary question, and a
    /// difficulty knob over a coin flip would be decoration.
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

    /// <summary>The band being played, blind. NEVER leaves the server until the round is answered.</summary>
    public Guid ArtistId { get; set; }

    /// <summary>
    /// The correct answer, snapshot when the round was dealt: the opponent's verdict on this band
    /// (<see cref="RiteState.Summoned"/> or <see cref="RiteState.Banished"/>) for the verdict game.
    /// Null for a kind whose truth is the band itself (guess the band), where the answer is
    /// <see cref="ArtistId"/> and a copy would be a second source of the same fact.
    /// </summary>
    public RiteState? Truth { get; set; }

    /// <summary>What the player answered. Null until they do — and the round's DTO stays blind while it is.</summary>
    public RiteState? Answer { get; set; }

    /// <summary>Whether <see cref="Answer"/> matched <see cref="Truth"/>. Null until answered.</summary>
    public bool? Correct { get; set; }

    public DateTimeOffset? AnsweredAt { get; set; }
}
