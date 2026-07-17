using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// The games (the GAMES wave). The first is the VERDICT game — "did your friend summon this band, or
/// banish it?": 45 blind seconds from an accepted friend's resolved rites, and you guess which way
/// they called it. It does not test whether you can name a band (naming rewards the canon, which is
/// what this app exists to argue with — D14/Ranks); it tests how well you know one person's ear.
///
/// Asynchronous and turn-based through the INBOX, never realtime (D60): you play your rounds when you
/// like, finishing drops a notification on your friend carrying your score, and they reply by playing
/// their own game back. Nothing here waits on anybody, and there is no socket.
///
/// Every round is served blind through the same machinery The Rite uses — a capability token, the
/// preview proxy (D32) and just-in-time preview resolution (D40) — and the band's identity does not
/// leave the server until the round is answered (see <see cref="GameView"/>).
///
/// Reading a friend's BANISHMENTS is new exposure: before this, a user's banishments were visible to
/// nobody but themselves (the Mirror, C20). So the game is gated on the opponent's explicit opt-in,
/// per the social block's guardrail.
/// </summary>
[ApiController]
[Route("api/games")]
[Authorize]
public class GamesController : ControllerBase
{
    private readonly GrimoireDbContext _db;
    private readonly FriendshipGuard _friends;
    private readonly PreviewProbe _probe;
    private readonly PreviewAudioProxy _audio;
    private readonly ArtistDetailBuilder _details;
    private readonly NotificationService _notifications;
    private readonly ILogger<GamesController> _logger;

    public GamesController(
        GrimoireDbContext db,
        FriendshipGuard friends,
        PreviewProbe probe,
        PreviewAudioProxy audio,
        ArtistDetailBuilder details,
        NotificationService notifications,
        ILogger<GamesController> logger)
    {
        _db = db;
        _friends = friends;
        _probe = probe;
        _audio = audio;
        _details = details;
        _notifications = notifications;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // Consent: whether friends may play this game against your grimoire
    // -----------------------------------------------------------------------

    /// <summary>The caller's own consent. Null means never asked — which the front shows as a question, not a no.</summary>
    [HttpGet("verdict/consent")]
    public async Task<ActionResult<VerdictGameConsentDto>> Consent(CancellationToken ct)
    {
        Guid me = CurrentUserId();

        bool? optIn = await _db.Users
            .Where(u => u.Id == me)
            .Select(u => u.VerdictGameOptIn)
            .FirstOrDefaultAsync(ct);

        return Ok(new VerdictGameConsentDto(optIn));
    }

    /// <summary>
    /// Sets the caller's consent. Turning it off does not delete the games already played — those
    /// rounds were dealt under a consent that was live at the time, and rewriting history would be a
    /// worse lie than the exposure. It stops any NEW game being dealt against them.
    /// </summary>
    [HttpPut("verdict/consent")]
    public async Task<IActionResult> SetConsent([FromBody] SetVerdictGameConsentRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Guid me = CurrentUserId();

        GrimoireUser? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == me, ct);

        if (user is null)
        {
            return NotFound(new { message = "No such user." });
        }

        user.VerdictGameOptIn = body.OptIn;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // -----------------------------------------------------------------------
    // The verdict game
    // -----------------------------------------------------------------------

    /// <summary>
    /// Whether a game can be dealt against a friend right now, and if not, why — asked before the
    /// front offers to play, so an unplayable friend renders a designed, honest sentence instead of a
    /// failed request. 403 when they are not an accepted friend.
    /// </summary>
    [HttpGet("verdict/availability/{friendId:guid}")]
    public async Task<ActionResult<VerdictGameAvailabilityDto>> Availability(Guid friendId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        if (!await _friends.AreAcceptedFriendsAsync(me, friendId, ct))
        {
            return Forbid();
        }

        if (!await HasOptedInAsync(friendId, ct))
        {
            // Their choice, and the front says so plainly: this is not an error and not their fault.
            return Ok(new VerdictGameAvailabilityDto(false, "opponent-has-not-opted-in", 0));
        }

        List<VerdictGamePool.Candidate> pool = await PoolAsync(friendId, ct);
        VerdictGameBlocker blocker = VerdictGamePool.Check(pool);

        return Ok(new VerdictGameAvailabilityDto(
            blocker == VerdictGameBlocker.None,
            blocker == VerdictGameBlocker.None ? null : GameView.ReasonKey(blocker),
            pool.Count));
    }

    /// <summary>
    /// Deals a new verdict game against an accepted friend who has opted in. The whole game is dealt
    /// at once — every round is checked audible here, so a game that starts is a game that can be
    /// finished. 403 when not friends or when they have not opted in; 409 when their resolved rites
    /// cannot make a game (with the machine-readable reason).
    /// </summary>
    [HttpPost("verdict")]
    public async Task<ActionResult<VerdictGameDto>> Start([FromBody] StartVerdictGameRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Guid me = CurrentUserId();
        Guid opponentId = body.OpponentId;

        if (opponentId == me)
        {
            return BadRequest(new { message = "You already know what you summoned." });
        }

        if (!await _friends.AreAcceptedFriendsAsync(me, opponentId, ct))
        {
            return Forbid();
        }

        // Re-checked on the write path, never trusted from the availability call: consent is the
        // whole gate on exposing a banishment, and a client could simply not have asked.
        if (!await HasOptedInAsync(opponentId, ct))
        {
            return Forbid();
        }

        List<VerdictGamePool.Candidate> pool = await PoolAsync(opponentId, ct);
        VerdictGameBlocker blocker = VerdictGamePool.Check(pool);

        if (blocker != VerdictGameBlocker.None)
        {
            return Conflict(new { reason = GameView.ReasonKey(blocker), message = "This friend's grimoire cannot make a game yet." });
        }

        Random rng = Random.Shared;

        // Walk each verdict's candidates in random order and keep the ones that can actually sound,
        // resolving previews just-in-time (D40) exactly as a serve does. Capped, so a big pool costs
        // a handful of probes and not a crawl — and in practice these bands were already probed when
        // The Rite served them to their owner, so the walk is usually pure cache.
        List<VerdictGamePool.Candidate> summons = await TakeAudibleAsync(
            VerdictGamePool.Shuffle(pool.Where(c => c.Verdict == RiteState.Summoned), rng),
            VerdictGamePool.MaxRounds,
            ct);

        List<VerdictGamePool.Candidate> banishments = await TakeAudibleAsync(
            VerdictGamePool.Shuffle(pool.Where(c => c.Verdict == RiteState.Banished), rng),
            VerdictGamePool.MaxRounds,
            ct);

        int rounds = VerdictGamePool.RoundsFor(summons.Count + banishments.Count);

        if (summons.Count == 0 || banishments.Count == 0 || rounds < VerdictGamePool.MinRounds)
        {
            // The verdicts existed but too few could be made to sound. Designed empty state (D25).
            return Conflict(new
            {
                reason = GameView.ReasonKey(VerdictGameBlocker.NotEnoughAudible),
                message = "Not enough of this friend's bands can be played right now.",
            });
        }

        IReadOnlyList<VerdictGamePool.Candidate> dealt = VerdictGamePool.Deal(summons, banishments, rounds, rng);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Game game = new()
        {
            Id = Guid.NewGuid(),
            Kind = GameKind.Verdict,
            PlayerId = me,
            OpponentId = opponentId,
            Difficulty = null,
            Status = GameStatus.InProgress,
            CreatedAt = now,
        };

        _db.Games.Add(game);

        for (int i = 0; i < dealt.Count; i++)
        {
            _db.GameRounds.Add(new GameRound
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                Ordinal = i,
                ArtistId = dealt[i].ArtistId,
                // Snapshot: the score can never drift under the player mid-game.
                Truth = dealt[i].Verdict,
            });
        }

        await _db.SaveChangesAsync(ct);

        VerdictGameDto? dto = await GameDtoAsync(game.Id, me, ct);

        if (dto is null)
        {
            return NotFound(new { message = "The game could not be read back." });
        }

        return Ok(dto);
    }

    /// <summary>
    /// Reads one of the caller's games — how the console resumes after a reload. Rounds not yet
    /// answered come back blind. 404 when the game is not the caller's: a game is readable only by
    /// the player who was dealt it, never by the friend being guessed (that would show them their own
    /// answers before the player gets there, and it is not their game).
    /// </summary>
    [HttpGet("verdict/{gameId:guid}")]
    public async Task<ActionResult<VerdictGameDto>> Game(Guid gameId, CancellationToken ct)
    {
        Guid me = CurrentUserId();

        VerdictGameDto? dto = await GameDtoAsync(gameId, me, ct);

        if (dto is null)
        {
            return NotFound(new { message = "No such game for this user." });
        }

        return Ok(dto);
    }

    /// <summary>
    /// The caller's verdict games, newest first: the ones they played and the ones played against
    /// them. Both sides are here because this list IS the turn — a friend's finished game is what the
    /// caller replies to by starting their own.
    /// </summary>
    [HttpGet("verdict")]
    public async Task<ActionResult<IReadOnlyList<VerdictGameSummaryDto>>> Games(CancellationToken ct)
    {
        Guid me = CurrentUserId();

        List<Game> games = await _db.Games
            .AsNoTracking()
            .Where(g => g.Kind == GameKind.Verdict && (g.PlayerId == me || g.OpponentId == me))
            .OrderByDescending(g => g.CreatedAt)
            .Take(MaxHistory)
            .ToListAsync(ct);

        if (games.Count == 0)
        {
            return Ok(Array.Empty<VerdictGameSummaryDto>());
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

        List<VerdictGameSummaryDto> summaries = games
            .Select(g =>
            {
                bool playedByMe = g.PlayerId == me;
                Guid other = playedByMe ? g.OpponentId!.Value : g.PlayerId;

                return new VerdictGameSummaryDto(
                    g.Id,
                    playedByMe,
                    other,
                    handles.TryGetValue(other, out string? h) ? h : null,
                    g.Status.ToString(),
                    g.CreatedAt,
                    GameView.Score(roundsByGame[g.Id].ToList()));
            })
            .ToList();

        return Ok(summaries);
    }

    /// <summary>
    /// Streams a round's band through the server. Capability URL: the token is the round id, so no
    /// auth header is needed and a plain audio element can play it, while the origin preview URL —
    /// which usually embeds the band name — never reaches the client. The same proxy and the same
    /// allow-list as The Rite (D32); this endpoint only chooses the row.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("rounds/{token:guid}/audio")]
    public async Task<IActionResult> Audio(Guid token, CancellationToken ct)
    {
        // Scoped to this kind's rounds. The second game (D67) picks its clip by a different rule — it
        // wants a track the listener has NOT heard — and a token redeemed across the two would quietly
        // serve the wrong one, at the one endpoint whose whole job is to serve the right one.
        string? previewUrl = await _db.GameRounds
            .Where(r => r.Id == token)
            .Join(_db.Games, r => r.GameId, g => g.Id, (r, g) => new { r.ArtistId, g.Kind })
            .Where(x => x.Kind == GameKind.Verdict)
            .Join(_db.Artists, x => x.ArtistId, a => a.Id, (x, a) => a.PreviewUrl)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(previewUrl))
        {
            return NotFound();
        }

        HttpResponseMessage? upstream = await _audio.OpenAsync(previewUrl, ct);

        if (upstream is null)
        {
            return NotFound();
        }

        // Let the framework dispose the upstream message once the response is written.
        HttpContext.Response.RegisterForDispose(upstream);

        Stream body = await upstream.Content.ReadAsStreamAsync(ct);
        string contentType = upstream.Content.Headers.ContentType?.ToString() ?? "audio/mpeg";

        return File(body, contentType);
    }

    /// <summary>
    /// Answers one round: was it a summon or a banishment? Scores the guess against the verdict
    /// snapshot at deal time, reveals the band at last, and — on the round that finishes the game —
    /// drops the result into the opponent's inbox, which is the turn hand-off. 400 on a verdict that
    /// is not summon/banish; 404 when the round is not the caller's; 409 when it was already
    /// answered (a round is answered once — otherwise the reveal would make the retry free).
    /// </summary>
    [HttpPost("rounds/{token:guid}/answer")]
    public async Task<ActionResult<AnswerRoundResultDto>> Answer(
        Guid token,
        [FromBody] AnswerRoundRequest body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);

        Guid me = CurrentUserId();

        if (!GameView.TryParseVerdict(body.Verdict, out RiteState answer))
        {
            return BadRequest(new { message = "Verdict must be 'summon' or 'banish'." });
        }

        GameRound? round = await _db.GameRounds.FirstOrDefaultAsync(r => r.Id == token, ct);

        if (round is null)
        {
            return NotFound(new { message = "No such round." });
        }

        Game? game = await _db.Games.FirstOrDefaultAsync(g => g.Id == round.GameId, ct);

        // The player of the game is the only one who may answer its rounds. A 404 rather than a 403:
        // a stranger holding a round token learns nothing about whether it exists.
        if (game is null || game.PlayerId != me)
        {
            return NotFound(new { message = "No such round for this user." });
        }

        if (round.AnsweredAt is not null)
        {
            return Conflict(new { message = "This round has already been answered." });
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        round.Answer = answer;
        round.Correct = round.Truth == answer;
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

        GameScoreDto score = GameView.Score(rounds);

        if (finished)
        {
            await NotifyOpponentAsync(game, score, ct);
        }

        ArtistDetailDto? reveal = await _details.BuildAsync(round.ArtistId, ct);

        return Ok(new AnswerRoundResultDto(
            round.Correct == true,
            round.Truth?.ToString() ?? string.Empty,
            reveal,
            score,
            finished));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>How many games the history list carries — a glanceable page, not an archive.</summary>
    private const int MaxHistory = 30;

    /// <summary>
    /// A user's pool for the verdict game: their RESOLVED rites, and only those. Summoned and
    /// Banished are verdicts; Served (dealt, never answered) and Again (a neutral skip) are not, and
    /// asking a player to guess a verdict that was never given would be inventing the answer.
    /// Projected in SQL — the artist rows carry a 768-dim vector, and materialising the pool to
    /// filter it in memory is the bug DECISIONS D61 found in ListenersJob.
    /// </summary>
    private async Task<List<VerdictGamePool.Candidate>> PoolAsync(Guid userId, CancellationToken ct)
    {
        return await _db.Rites
            .AsNoTracking()
            .Where(r => r.UserId == userId
                && (r.State == RiteState.Summoned || r.State == RiteState.Banished))
            .Select(r => new VerdictGamePool.Candidate(r.ArtistId, r.State))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Walks candidates in the order given and keeps up to <paramref name="max"/> that can actually
    /// sound, resolving previews just-in-time (D40) through the shared probe. Stops as soon as it has
    /// enough, so the network cost is bounded by what a game needs and not by the pool's size.
    ///
    /// The artist rows are fetched ONE AT A TIME, and that is deliberate rather than an N+1 left
    /// lying around: an <c>Artist</c> carries a 768-dim embedding, so batching the whole shuffled
    /// pool would drag hundreds of vectors across the wire to look at a <c>preview_url</c> on the
    /// first handful — the shape of the bug DECISIONS D61 found in ListenersJob. The loop breaks at
    /// <paramref name="max"/>, so it costs a bounded few round trips and reads only what it needs.
    /// </summary>
    private async Task<List<VerdictGamePool.Candidate>> TakeAudibleAsync(
        IReadOnlyList<VerdictGamePool.Candidate> candidates,
        int max,
        CancellationToken ct)
    {
        List<VerdictGamePool.Candidate> audible = new(max);
        bool mutated = false;

        foreach (VerdictGamePool.Candidate candidate in candidates)
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

            ProbeOutcome outcome = await _probe.EnsureAudibleAsync(artist, ct);
            mutated |= PreviewProbe.Mutated(outcome);

            if (PreviewProbe.IsAudible(outcome))
            {
                audible.Add(candidate);
            }
        }

        if (mutated)
        {
            // The probe's cache writes are saved even if this game never starts — the work is real
            // and the next draw should not repeat it.
            await _db.SaveChangesAsync(ct);
        }

        return audible;
    }

    /// <summary>Whether a user has explicitly allowed the game. Null (never asked) is not consent.</summary>
    private async Task<bool> HasOptedInAsync(Guid userId, CancellationToken ct)
    {
        bool? optIn = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.VerdictGameOptIn)
            .FirstOrDefaultAsync(ct);

        return optIn == true;
    }

    /// <summary>
    /// Builds a game's DTO for its player, blind rounds and all. Returns null when the game is not
    /// this user's. The artist summaries are fetched ONLY for answered rounds — an unanswered round
    /// has no band to name, so its name is never even loaded.
    /// </summary>
    private async Task<VerdictGameDto?> GameDtoAsync(Guid gameId, Guid me, CancellationToken ct)
    {
        // Kind is filtered, not assumed. Since the second game (D67) shares these tables, "a game of
        // this user's" stopped being the same thing as "a verdict game of this user's" — and a
        // guess-the-band game read through here would come back shaped as something it is not.
        Game? game = await _db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameId && g.PlayerId == me && g.Kind == GameKind.Verdict, ct);

        if (game is null || game.OpponentId is null)
        {
            return null;
        }

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

        Dictionary<Guid, string?> handles = await HandlesAsync([game.OpponentId.Value], ct);

        List<GameRoundDto> roundDtos = rounds
            .Select(r => GameView.Round(
                r,
                AudioUrlFor(r.Id),
                artists.TryGetValue(r.ArtistId, out ArtistSummaryDto? a) ? a : null))
            .ToList();

        return new VerdictGameDto(
            game.Id,
            game.OpponentId.Value,
            handles.TryGetValue(game.OpponentId.Value, out string? handle) ? handle : null,
            game.Status.ToString(),
            game.CreatedAt,
            game.FinishedAt,
            roundDtos,
            GameView.Score(rounds));
    }

    /// <summary>
    /// Tells the opponent their ear was guessed, and how well — the turn hand-off (D60). Best-effort
    /// by contract: the answer is already committed, so a failed notification must never surface as a
    /// failed answer. A swallowed, logged failure costs an inbox line; throwing here would cost the
    /// player the round they just played.
    /// </summary>
    private async Task NotifyOpponentAsync(Game game, GameScoreDto score, CancellationToken ct)
    {
        if (game.OpponentId is null)
        {
            return;
        }

        try
        {
            await _notifications.CreateAsync(
                game.OpponentId.Value,
                NotificationType.VerdictGamePlayed,
                game.PlayerId,
                new NotificationPayload.VerdictGamePlayed(game.Id, score.Correct, score.Total),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Verdict-game notification failed after a finished game; the game itself stands.");
        }
    }

    /// <summary>The capability audio URL for a round (the origin preview URL never reaches the client).</summary>
    private string AudioUrlFor(Guid roundId)
    {
        return $"{Request.Scheme}://{Request.Host}/api/games/rounds/{roundId}/audio";
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
