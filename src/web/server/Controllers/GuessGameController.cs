using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// "Guess the band" (D67) — the second game, and the joke it is built on: <b>you loved this one blind,
/// with no name and no cover. Do you have the faintest idea who it is?</b>
///
/// <para>
/// It is played over the player's OWN summons and nobody else's. A general "name this band" quiz was
/// refused twice (D43, D66) and the reason is structural, not squeamish: you can only name what you
/// already know, so a name quiz scores the canon you arrived with — which is the exact axis this app
/// exists to invert (discovering Metallica is worth nothing). The catalogue makes it plainer still:
/// 31 752 of the bands in the pool are Nameless and most have no biography at all. There is nothing to
/// "know" about them, and pretending otherwise would be a quiz about Wikipedia. Bounded to your own
/// grimoire the question turns around and becomes one only you can answer, and one your ears already
/// answered once.
/// </para>
/// <para>
/// It reuses the verdict game's tables whole (<c>games</c>, <c>game_rounds</c>): <see cref="GameKind"/>
/// discriminates, <c>opponent_id</c> was left nullable so the solo mode would cost nothing, and
/// <c>difficulty</c> was modelled before its first row existed. Adding this game required no migration
/// and no backfill, which was the entire point of D66's schema (MEMORY §6f: the one thing migrations
/// here must never do is move data).
/// </para>
/// <para>
/// <b>No consent gate, and that is not an oversight.</b> D66 needed one because it exposed a friend's
/// BANISHMENTS — a negative judgement no endpoint had ever shown to anyone but its author. This game
/// reads nothing of the opponent's: both sides play their own summons, and only the scores meet. All
/// it needs is an accepted friendship, which is what any inbox line already needs (D60).
/// </para>
/// <para>
/// Rounds are blind through the same machinery as everything else here — a capability token and the
/// preview proxy (D32), previews resolved just-in-time (D40), never a byte of audio stored (Invariant
/// 4). Whether the band is named is decided in exactly one place, <see cref="GameView"/>.
/// </para>
/// </summary>
[ApiController]
[Route("api/games/guess")]
[Authorize]
public class GuessGameController : ControllerBase
{
    private readonly GrimoireDbContext _db;
    private readonly FriendshipGuard _friends;
    private readonly IGuessPreviewSource _clips;
    private readonly PreviewAudioProxy _audio;
    private readonly ArtistDetailBuilder _details;
    private readonly NotificationService _notifications;
    private readonly ILogger<GuessGameController> _logger;

    public GuessGameController(
        GrimoireDbContext db,
        FriendshipGuard friends,
        IGuessPreviewSource clips,
        PreviewAudioProxy audio,
        ArtistDetailBuilder details,
        NotificationService notifications,
        ILogger<GuessGameController> logger)
    {
        _db = db;
        _friends = friends;
        _clips = clips;
        _audio = audio;
        _details = details;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>How many games the history list carries — a glanceable page, not an archive.</summary>
    private const int MaxHistory = 30;

    /// <summary>
    /// How many of the player's nearest summons are pulled as decoy material. Three are used; the rest
    /// are slack for the ones that turn out to be the answer of another round or to have no name worth
    /// showing. Bounded because these rows are only wanted for their names, and a grimoire is small.
    /// </summary>
    private const int NeighbourPool = 12;

    // -----------------------------------------------------------------------
    // Availability
    // -----------------------------------------------------------------------

    /// <summary>
    /// Whether the caller's own grimoire can make a game at this difficulty, and if not, why. There is
    /// no per-friend version of this, and that absence says something true: whose grimoire is playable
    /// has nothing to do with who you challenge, because a challenge never reads their side. The only
    /// thing a friend must be is a friend, which the friends list already knows.
    /// </summary>
    [HttpGet("availability")]
    public async Task<ActionResult<GuessGameAvailabilityDto>> Availability(
        [FromQuery] string difficulty = "normal",
        CancellationToken ct = default)
    {
        if (!GameView.TryParseDifficulty(difficulty, out GameDifficulty parsed))
        {
            return BadRequest(new { message = "Difficulty must be 'normal' or 'hard'." });
        }

        Guid me = CurrentUserId();

        int summons = await _db.Rites
            .AsNoTracking()
            .CountAsync(r => r.UserId == me && r.State == RiteState.Summoned, ct);

        GuessGameBlocker blocker = GuessGamePool.Check(summons, parsed);

        return Ok(new GuessGameAvailabilityDto(
            blocker == GuessGameBlocker.None,
            blocker == GuessGameBlocker.None ? null : GameView.ReasonKey(blocker),
            summons));
    }

    // -----------------------------------------------------------------------
    // Dealing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Deals a new game over the caller's own grimoire. The whole thing is dealt at once and every
    /// round is checked audible here, so a game that starts is a game that can be finished. 400 on an
    /// unknown difficulty; 403 when a challenged opponent is not an accepted friend; 409 with a
    /// machine-readable reason when the caller's grimoire cannot make a game yet.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<GuessGameDto>> Start([FromBody] StartGuessGameRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (!GameView.TryParseDifficulty(body.Difficulty, out GameDifficulty difficulty))
        {
            return BadRequest(new { message = "Difficulty must be 'normal' or 'hard'." });
        }

        Guid me = CurrentUserId();
        Guid? opponentId = body.OpponentId;

        if (opponentId == me)
        {
            return BadRequest(new { message = "Play it solo, or play it against somebody else." });
        }

        if (opponentId is not null && !await _friends.AreAcceptedFriendsAsync(me, opponentId.Value, ct))
        {
            return Forbid();
        }

        List<GuessGamePool.Candidate> pool = await PoolAsync(me, ct);
        GuessGameBlocker blocker = GuessGamePool.Check(pool.Count, difficulty);

        if (blocker != GuessGameBlocker.None)
        {
            return Conflict(new { reason = GameView.ReasonKey(blocker), message = "Your grimoire cannot make a game yet." });
        }

        // Which bands become rounds is a fresh draw every game — this is the ONE place a real random is
        // wanted, and it is safe here because the outcome is written down (the rounds are rows). What
        // must never be random is anything recomputed on a read: the choices, which are not stored.
        List<GuessGamePool.Candidate> audible = await TakeAudibleAsync(
            VerdictGamePool.Shuffle(pool, Random.Shared),
            GuessGamePool.MaxRounds,
            ct);

        int rounds = GuessGamePool.RoundsFor(audible.Count);

        if (rounds < GuessGamePool.MinRounds)
        {
            // The summons existed but too few could be made to sound. Designed empty state (D25).
            return Conflict(new
            {
                reason = GameView.ReasonKey(GuessGameBlocker.NotEnoughAudible),
                message = "Too few of the bands you summoned can be played right now.",
            });
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Game game = new()
        {
            Id = Guid.NewGuid(),
            Kind = GameKind.GuessBand,
            PlayerId = me,
            OpponentId = opponentId,
            Difficulty = difficulty,
            Status = GameStatus.InProgress,
            CreatedAt = now,
        };

        _db.Games.Add(game);

        for (int i = 0; i < rounds; i++)
        {
            _db.GameRounds.Add(new GameRound
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                Ordinal = i,
                ArtistId = audible[i].ArtistId,
                // Null, and deliberately: this kind's truth IS the band, and a copy of it here would be
                // a second source of the same fact, free to drift from the first (D66).
                Truth = null,
            });
        }

        await _db.SaveChangesAsync(ct);

        GuessGameDto? dto = await GameDtoAsync(game.Id, me, ct);

        if (dto is null)
        {
            return NotFound(new { message = "The game could not be read back." });
        }

        return Ok(dto);
    }

    /// <summary>
    /// Reads one of the caller's games — how the console resumes after a reload. Unanswered rounds come
    /// back blind, and their multiple choice comes back in the SAME order it was first served: the
    /// order is a pure function of the round's id, so two reads cannot be intersected to find the name
    /// that appears in both. 404 when the game is not the caller's.
    /// </summary>
    [HttpGet("{gameId:guid}")]
    public async Task<ActionResult<GuessGameDto>> Game(Guid gameId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        GuessGameDto? dto = await GameDtoAsync(gameId, me, ct);

        if (dto is null)
        {
            return NotFound(new { message = "No such game for this user." });
        }

        return Ok(dto);
    }

    /// <summary>
    /// The caller's guess games, newest first: the ones they played and the ones friends challenged
    /// them with. Both sides are here because this list IS the turn — a friend's finished game is what
    /// the caller answers by playing their own, and it is where the two scores can finally be read next
    /// to each other.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GuessGameSummaryDto>>> Games(CancellationToken ct)
    {
        Guid me = CurrentUserId();

        List<Game> games = await _db.Games
            .AsNoTracking()
            .Where(g => g.Kind == GameKind.GuessBand && (g.PlayerId == me || g.OpponentId == me))
            .OrderByDescending(g => g.CreatedAt)
            .Take(MaxHistory)
            .ToListAsync(ct);

        if (games.Count == 0)
        {
            return Ok(Array.Empty<GuessGameSummaryDto>());
        }

        List<Guid> gameIds = games.Select(g => g.Id).ToList();

        // One query for every round of the page, grouped in memory: a round trip per game would be a
        // query per row for a list that exists to be glanced at.
        ILookup<Guid, GameRound> roundsByGame = (await _db.GameRounds
                .AsNoTracking()
                .Where(r => gameIds.Contains(r.GameId))
                .ToListAsync(ct))
            .ToLookup(r => r.GameId);

        List<Guid> otherIds = games
            .Select(g => g.PlayerId == me ? g.OpponentId : g.PlayerId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string?> handles = await HandlesAsync(otherIds, ct);

        List<GuessGameSummaryDto> summaries = games
            .Select(g =>
            {
                bool playedByMe = g.PlayerId == me;
                Guid? other = playedByMe ? g.OpponentId : g.PlayerId;

                return new GuessGameSummaryDto(
                    g.Id,
                    playedByMe,
                    (g.Difficulty ?? GameDifficulty.Normal).ToString(),
                    other,
                    other is not null && handles.TryGetValue(other.Value, out string? h) ? h : null,
                    g.Status.ToString(),
                    g.CreatedAt,
                    GameView.GuessScore(roundsByGame[g.Id].ToList(), g.Difficulty ?? GameDifficulty.Normal));
            })
            .ToList();

        return Ok(summaries);
    }

    // -----------------------------------------------------------------------
    // Audio
    // -----------------------------------------------------------------------

    /// <summary>
    /// Streams a round's band through the server. Capability URL: the token is the round id, so no auth
    /// header is needed and a plain audio element can play it, while the origin preview URL — which
    /// routinely spells the band's name in its path, and here the band's name IS the answer — never
    /// reaches the client. The same proxy and the same allow-list as The Rite (D32), re-validated
    /// inside <see cref="PreviewAudioProxy.OpenAsync"/>: this endpoint only chooses the row.
    ///
    /// <para>
    /// It has its own route rather than reusing the verdict game's because the clip is chosen
    /// differently: this game wants a track the player has NOT heard, and asking for it is the whole
    /// difference between testing knowledge of a band and testing memory of one 45-second file (D67).
    /// Kind is checked, so a token from the other game cannot be redeemed here.
    /// </para>
    /// </summary>
    [AllowAnonymous]
    [HttpGet("rounds/{token:guid}/audio")]
    public async Task<IActionResult> Audio(Guid token, CancellationToken ct)
    {
        Guid? artistId = await _db.GameRounds
            .Where(r => r.Id == token)
            .Join(_db.Games, r => r.GameId, g => g.Id, (r, g) => new { r.ArtistId, g.Kind })
            .Where(x => x.Kind == GameKind.GuessBand)
            .Select(x => (Guid?)x.ArtistId)
            .FirstOrDefaultAsync(ct);

        if (artistId is null)
        {
            return NotFound();
        }

        // Tracked: a just-in-time resolve caches the preview on the row, and that write is real work
        // this request paid for (D40).
        Artist? artist = await _db.Artists.FirstOrDefaultAsync(a => a.Id == artistId.Value, ct);

        if (artist is null)
        {
            return NotFound();
        }

        string? heard = artist.PreviewUrl;
        GuessClip? clip = await _clips.ChooseAsync(artist, heard, token, ct);

        await _db.SaveChangesAsync(ct);

        if (clip is null)
        {
            return NotFound();
        }

        if (!clip.IsDifferentTrack)
        {
            // Worth seeing in the logs: it means the round is asking about a band whose only clip the
            // player has already heard, so it is measuring recall rather than knowledge. Not an error —
            // the harvest simply has not reached this band, or the band honestly has one clip.
            _logger.LogInformation("A guess round is replaying the clip its player already heard: no alternate is stored.");
        }

        HttpResponseMessage? upstream = await _audio.OpenAsync(clip.Url, ct);

        if (upstream is null)
        {
            return NotFound();
        }

        // Let the framework dispose the upstream message once the response is written.
        HttpContext.Response.RegisterForDispose(upstream);

        Stream stream = await upstream.Content.ReadAsStreamAsync(ct);
        string contentType = upstream.Content.Headers.ContentType?.ToString() ?? "audio/mpeg";

        return File(stream, contentType);
    }

    // -----------------------------------------------------------------------
    // Answering
    // -----------------------------------------------------------------------

    /// <summary>
    /// Answers one round — by picking a name in Normal, or by typing one in Hard — scores it, reveals
    /// the band, and on the round that finishes a challenged game drops the score into the friend's
    /// inbox, which is the turn hand-off (D60). 400 when the body does not match the game's difficulty;
    /// 404 when the round is not the caller's; 409 when it was already answered — a round is answered
    /// once, or the reveal would make the retry free and the score would mean nothing.
    /// </summary>
    [HttpPost("rounds/{token:guid}/answer")]
    public async Task<ActionResult<AnswerGuessRoundResultDto>> Answer(
        Guid token,
        [FromBody] AnswerGuessRoundRequest body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Guid me = CurrentUserId();

        GameRound? round = await _db.GameRounds.FirstOrDefaultAsync(r => r.Id == token, ct);

        if (round is null)
        {
            return NotFound(new { message = "No such round." });
        }

        Game? game = await _db.Games.FirstOrDefaultAsync(g => g.Id == round.GameId, ct);

        // The player of the game is the only one who may answer its rounds. A 404 rather than a 403: a
        // stranger holding a round token learns nothing about whether it exists.
        if (game is null || game.Kind != GameKind.GuessBand || game.PlayerId != me)
        {
            return NotFound(new { message = "No such round for this user." });
        }

        if (round.AnsweredAt is not null)
        {
            return Conflict(new { message = "This round has already been answered." });
        }

        GameDifficulty difficulty = game.Difficulty ?? GameDifficulty.Normal;

        Artist? artist = await _db.Artists
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == round.ArtistId, ct);

        if (artist is null)
        {
            return NotFound(new { message = "The round's band is gone." });
        }

        bool correct;

        if (difficulty == GameDifficulty.Normal)
        {
            if (body.ArtistId is null)
            {
                return BadRequest(new { message = "This round is a multiple choice: answer with one of its artistIds." });
            }

            IReadOnlyList<GuessGamePool.Candidate> choices = await ChoicesAsync(round, me, game.CreatedAt, ct);

            // The pick must be one of the four actually offered. A player cannot reach the answer by
            // posting ids at it — a round is answered once — but an id from outside the choices is a
            // broken client rather than a wrong guess, and the two should not score the same.
            if (!choices.Any(c => c.ArtistId == body.ArtistId.Value))
            {
                return BadRequest(new { message = "That is not one of this round's choices." });
            }

            correct = body.ArtistId.Value == round.ArtistId;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(body.Name))
            {
                return BadRequest(new { message = "This round is typed: answer with the band's name." });
            }

            if (body.ArtistId is not null)
            {
                // Hard has no choices, so there is no id to legitimately hold. Accepting one would turn
                // the hard mode into the easy one with the answers hidden — and pay it triple.
                return BadRequest(new { message = "This round is typed: an artistId is not an answer here." });
            }

            correct = await JudgeTypedNameAsync(body.Name, artist, me, game.CreatedAt, ct);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Answer stays null: a band does not fit in that column, and truncating one to fit would file a
        // different fact under its name. Correct is decided here, once, against the real band.
        round.Correct = correct;
        round.AnsweredAt = now;

        List<GameRound> rounds = await _db.GameRounds
            .Where(r => r.GameId == game.Id)
            .ToListAsync(ct);

        bool finished = rounds.All(r => r.AnsweredAt is not null);

        if (finished && game.Status != GameStatus.Finished)
        {
            game.Status = GameStatus.Finished;
            game.FinishedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        GuessScoreDto score = GameView.GuessScore(rounds, difficulty);

        if (finished)
        {
            await NotifyOpponentAsync(game, score, ct);
        }

        ArtistDetailDto? reveal = await _details.BuildAsync(round.ArtistId, ct);

        return Ok(new AnswerGuessRoundResultDto(correct, reveal, score, finished));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// The player's pool: the bands they SUMMONED, and only those. A summon is the only rite that means
    /// "I chose this" — Served was never answered, Again is a neutral skip, and a Banished band is one
    /// they rejected in 45 seconds, whose name would test nothing and would quietly turn the game back
    /// into the trivia quiz it exists instead of (D67).
    ///
    /// <para>
    /// Projected in SQL down to an id and a name. The artist rows carry a 768-dimension vector each,
    /// and materialising a pool to read a name off it is the bug D61 found in <c>ListenersJob</c> and
    /// D65 found again in <c>InfluenceJob</c>.
    /// </para>
    /// </summary>
    private async Task<List<GuessGamePool.Candidate>> PoolAsync(Guid userId, CancellationToken ct)
    {
        return await _db.Rites
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.State == RiteState.Summoned)
            .Join(_db.Artists, r => r.ArtistId, a => a.Id, (r, a) => new GuessGamePool.Candidate(a.Id, a.Name))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Walks candidates in the order given and keeps up to <paramref name="max"/> whose band can
    /// actually sound, resolving previews just-in-time (D40) through the shared contract. Stops as soon
    /// as it has enough, so the network cost is bounded by what a game needs rather than by the
    /// grimoire's size — and in practice every one of these bands was probed when The Rite served it to
    /// this very player, so the walk is usually pure cache.
    ///
    /// <para>
    /// The artist rows are fetched ONE AT A TIME, deliberately rather than as an N+1 left lying around:
    /// an <c>Artist</c> carries a 768-dimension embedding, so batching the whole shuffled pool would
    /// drag vectors across the wire to look at a <c>preview_url</c> on the first handful. The loop
    /// breaks at <paramref name="max"/>.
    /// </para>
    /// </summary>
    private async Task<List<GuessGamePool.Candidate>> TakeAudibleAsync(
        IReadOnlyList<GuessGamePool.Candidate> candidates,
        int max,
        CancellationToken ct)
    {
        List<GuessGamePool.Candidate> audible = new(max);

        foreach (GuessGamePool.Candidate candidate in candidates)
        {
            if (audible.Count >= max)
            {
                break;
            }

            Artist? artist = await _db.Artists.FirstOrDefaultAsync(a => a.Id == candidate.ArtistId, ct);

            if (artist is null)
            {
                continue;
            }

            GuessClip? clip = await _clips.ChooseAsync(artist, artist.PreviewUrl, candidate.ArtistId, ct);

            if (clip is not null)
            {
                audible.Add(candidate);
            }
        }

        // The probe's cache writes are saved even if this game never starts — the work is real and the
        // next draw should not repeat it.
        await _db.SaveChangesAsync(ct);

        return audible;
    }

    /// <summary>
    /// The four names one Normal round offers: the band, and its three nearest neighbours in the
    /// player's own grimoire.
    ///
    /// <para>
    /// <b>Near, because far is not a game.</b> The decoys come from the map the whole app is built on
    /// (centred embeddings, D26/D31), so they are the bands that actually sound like the answer — which
    /// is the difference between a round that is hard and a round where three obviously-wrong names sit
    /// next to one plausible one. pgvector gives the ordering for free.
    /// </para>
    /// <para>
    /// <b>Nothing here may move.</b> The choices are not stored — there is no column for them — so this
    /// runs on every read of the round, and if two reads could disagree, a player could reload and
    /// intersect the draws to find the one name in both. Two things guarantee it cannot:
    /// the pool is frozen to rites resolved at or before the game was dealt, and a resolved rite is
    /// immutable in this codebase (<c>RiteController</c> only ever writes a state onto a rite that is
    /// still <c>Served</c>, and only ever deletes rites that are still <c>Served</c> — so a summon,
    /// once made, can neither change nor vanish). The ordering is then a pure hash of the round's id.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<GuessGamePool.Candidate>> ChoicesAsync(
        GameRound round,
        Guid playerId,
        DateTimeOffset dealtAt,
        CancellationToken ct)
    {
        var answer = await _db.Artists
            .AsNoTracking()
            .Where(a => a.Id == round.ArtistId)
            .Select(a => new { a.Id, a.Name, a.Embedding })
            .FirstOrDefaultAsync(ct);

        if (answer is null)
        {
            return [];
        }

        GuessGamePool.Candidate correct = new(answer.Id, answer.Name);

        // The decoy universe: everything else this player had summoned when the game was dealt. Later
        // summons are excluded so that playing The Rite in another tab cannot reshape a live round's
        // choices — the one way the pool could otherwise move underneath it.
        IQueryable<Artist> universe = _db.Rites
            .AsNoTracking()
            .Where(r => r.UserId == playerId
                && r.State == RiteState.Summoned
                && r.ResolvedAt != null
                && r.ResolvedAt <= dealtAt)
            .Join(_db.Artists, r => r.ArtistId, a => a.Id, (r, a) => a)
            .Where(a => a.Id != round.ArtistId);

        List<GuessGamePool.Candidate> neighbours;

        if (answer.Embedding is not null)
        {
            Vector target = answer.Embedding;

            neighbours = await universe
                .Where(a => a.Embedding != null)
                .OrderBy(a => a.Embedding!.CosineDistance(target))
                // The tiebreak is not cosmetic: without a total order, two equidistant bands could come
                // back in either order and the choices would stop being reproducible.
                .ThenBy(a => a.Id)
                .Take(NeighbourPool)
                .Select(a => new GuessGamePool.Candidate(a.Id, a.Name))
                .ToListAsync(ct);
        }
        else
        {
            // No vector, no neighbourhood. The round degrades to arbitrary decoys rather than breaking
            // (Invariant 5) — an easier round, honestly dealt.
            List<GuessGamePool.Candidate> all = await universe
                .Select(a => new GuessGamePool.Candidate(a.Id, a.Name))
                .ToListAsync(ct);

            neighbours = GuessGamePool.ArbitraryOrder(round.Id, all).ToList();
        }

        return GuessGamePool.Choices(round.Id, correct, neighbours);
    }

    /// <summary>
    /// Judges a typed name (Hard). Generous where generosity is safe and hard-nosed where it is not:
    /// accents and case are folded away before anything is measured, a typo is forgiven within a budget
    /// — and the name of a DIFFERENT band is never accepted, however close it sits.
    ///
    /// <para>
    /// "A different band" is checked twice over, against two different populations. First the player's
    /// own grimoire, which is this game's universe of confusable answers and the very set the
    /// multiple-choice decoys are drawn from. Then the catalogue itself, through the trigram index the
    /// artist search has always used (<c>pg_trgm</c>, GIN) — because the bands a player might name are
    /// not limited to the ones they have summoned. Somebody typing "Mayhemic" when the answer is
    /// "Mayhem" named a real and different band, and only the catalogue knows that.
    /// </para>
    /// </summary>
    private async Task<bool> JudgeTypedNameAsync(
        string typed,
        Artist answer,
        Guid playerId,
        DateTimeOffset dealtAt,
        CancellationToken ct)
    {
        string needle = typed.Trim();

        if (needle.Length > SearchNeedle.MaxLength)
        {
            needle = needle[..SearchNeedle.MaxLength];
        }

        // The player's other summons: what this game could plausibly be asking about.
        List<string> grimoire = await _db.Rites
            .AsNoTracking()
            .Where(r => r.UserId == playerId
                && r.State == RiteState.Summoned
                && r.ResolvedAt != null
                && r.ResolvedAt <= dealtAt)
            .Join(_db.Artists, r => r.ArtistId, a => a.Id, (r, a) => a)
            .Where(a => a.Id != answer.Id)
            .Select(a => a.Name)
            .ToListAsync(ct);

        // And the catalogue's near names, off the trigram index. Bounded: this only needs to know
        // whether the typed string IS some other band, and the index ranks the plausible ones first.
        List<string> nearby = await _db.Artists
            .AsNoTracking()
            .Where(a => a.Id != answer.Id && EF.Functions.TrigramsAreSimilar(a.Name, needle))
            .OrderByDescending(a => EF.Functions.TrigramsSimilarity(a.Name, needle))
            .ThenBy(a => a.Name)
            .Take(10)
            .Select(a => a.Name)
            .ToListAsync(ct);

        return GuessMatch.IsCorrect(needle, answer.Name, grimoire.Concat(nearby));
    }

    /// <summary>
    /// Builds a game's DTO for its player, blind rounds and all. Returns null when the game is not this
    /// user's, or is not this kind. The artist summaries are fetched ONLY for answered rounds — an
    /// unanswered round has no band to name, so its name is never even loaded.
    /// </summary>
    private async Task<GuessGameDto?> GameDtoAsync(Guid gameId, Guid me, CancellationToken ct)
    {
        Game? game = await _db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameId && g.PlayerId == me && g.Kind == GameKind.GuessBand, ct);

        if (game is null)
        {
            return null;
        }

        GameDifficulty difficulty = game.Difficulty ?? GameDifficulty.Normal;

        List<GameRound> rounds = await _db.GameRounds
            .AsNoTracking()
            .Where(r => r.GameId == gameId)
            .OrderBy(r => r.Ordinal)
            .ToListAsync(ct);

        List<Guid> revealedIds = rounds
            .Where(GameView.IsRevealed)
            .Select(r => r.ArtistId)
            .ToList();

        Dictionary<Guid, ArtistSummaryDto> artists = revealedIds.Count == 0
            ? []
            : await _db.Artists
                .AsNoTracking()
                .Where(a => revealedIds.Contains(a.Id))
                .Select(a => new ArtistSummaryDto(a.Id, a.Name, a.Country, a.FormedYear, a.Rank))
                .ToDictionaryAsync(a => a.Id, ct);

        List<GuessRoundDto> roundDtos = [];

        foreach (GameRound round in rounds)
        {
            IReadOnlyList<GuessChoiceDto>? choices = null;

            if (difficulty == GameDifficulty.Normal)
            {
                choices = (await ChoicesAsync(round, me, game.CreatedAt, ct))
                    .Select(c => new GuessChoiceDto(c.ArtistId, c.Name))
                    .ToList();
            }

            roundDtos.Add(GameView.GuessRound(
                round,
                AudioUrlFor(round.Id),
                choices,
                artists.TryGetValue(round.ArtistId, out ArtistSummaryDto? a) ? a : null));
        }

        string? handle = null;

        if (game.OpponentId is not null)
        {
            Dictionary<Guid, string?> handles = await HandlesAsync([game.OpponentId.Value], ct);
            handle = handles.TryGetValue(game.OpponentId.Value, out string? h) ? h : null;
        }

        return new GuessGameDto(
            game.Id,
            difficulty.ToString(),
            game.OpponentId,
            handle,
            game.Status.ToString(),
            game.CreatedAt,
            game.FinishedAt,
            roundDtos,
            GameView.GuessScore(rounds, difficulty));
    }

    /// <summary>
    /// Tells a challenged friend the score to beat — the turn hand-off (D60). Solo games notify nobody,
    /// which is the whole of what "solo" means here. Best-effort by contract: the answer is already
    /// committed, so a failed notification must never surface as a failed answer. A swallowed, logged
    /// failure costs an inbox line; throwing here would cost the player the round they just played.
    /// </summary>
    private async Task NotifyOpponentAsync(Game game, GuessScoreDto score, CancellationToken ct)
    {
        if (game.OpponentId is null)
        {
            return;
        }

        try
        {
            await _notifications.CreateAsync(
                game.OpponentId.Value,
                NotificationType.GuessGamePlayed,
                game.PlayerId,
                new NotificationPayload.GuessGamePlayed(game.Id, score.Correct, score.Total),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guess-game notification failed after a finished game; the game itself stands.");
        }
    }

    /// <summary>The capability audio URL for a round (the origin preview URL never reaches the client).</summary>
    private string AudioUrlFor(Guid roundId)
    {
        return $"{Request.Scheme}://{Request.Host}/api/games/guess/rounds/{roundId}/audio";
    }

    /// <summary>Public handles for a set of user ids (missing ids simply absent from the map).</summary>
    private async Task<Dictionary<Guid, string?>> HandlesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Handle })
            .ToDictionaryAsync(u => u.Id, u => u.Handle, ct);
    }

    private Guid CurrentUserId()
    {
        // MapInboundClaims is off, so the subject is the raw "sub" claim.
        string? sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(sub, out Guid id))
        {
            return id;
        }

        throw new InvalidOperationException("Authenticated request carries no usable subject claim.");
    }
}
