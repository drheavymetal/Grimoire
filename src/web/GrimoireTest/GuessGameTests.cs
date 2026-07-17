using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// "Guess the band" (D67) end-to-end against a real PostgreSQL and real HTTP: that the pool is the
/// player's OWN summons and nothing else, that the multiple choice cannot be read for the answer, that
/// a typed name is judged generously but not loosely, that the two difficulties are priced apart, and
/// that the empty states are honest. Uses a throwaway database it creates and drops, never the
/// development one. No network beyond the API: every fixture band carries a cached, allow-listed
/// preview URL, so the just-in-time resolver short-circuits on the cache and never reaches iTunes.
/// Skipped cleanly when PostgreSQL is down.
/// </summary>
public class GuessGameTests : IAsyncLifetime
{
    private const string MaintenanceConnectionString =
        "Host=localhost;Port=5433;Database=grimoire;Username=grimoire;Password=grimoire;Timeout=3;Command Timeout=5";

    private const string AllowlistedPreview = "https://audio-ssl.itunes.apple.com/preview/fixture.m4a";

    private readonly string _databaseName = $"grimoire_test_guess_{Guid.NewGuid():N}";

    private string TestConnectionString =>
        $"Host=localhost;Port=5433;Database={_databaseName};Username=grimoire;Password=grimoire";

    private bool _databaseReady;
    private string _skipReason = "PostgreSQL is not reachable on localhost:5433 (start build/dev/docker-compose.yml).";

    public async Task InitializeAsync()
    {
        try
        {
            await using NpgsqlConnection maintenance = new(MaintenanceConnectionString);
            await maintenance.OpenAsync();

            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE);");
            await ExecuteAsync(maintenance, $"CREATE DATABASE {_databaseName};");

            _databaseReady = true;
        }
        catch (NpgsqlException ex)
        {
            _skipReason = $"Could not provision the '{_databaseName}' database: {ex.Message}";
            _databaseReady = false;
        }
        catch (SocketException ex)
        {
            _skipReason = $"PostgreSQL is not reachable on localhost:5433: {ex.Message}";
            _databaseReady = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_databaseReady)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();

        await using NpgsqlConnection maintenance = new(MaintenanceConnectionString);
        await maintenance.OpenAsync();
        await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE);");
    }

    // -----------------------------------------------------------------------
    // The pool: your own summons, and nothing else at all
    // -----------------------------------------------------------------------

    /// <summary>
    /// THE pool rule, and the one that keeps this from being the trivia quiz D43/D66 refused. Only a
    /// SUMMON is a band you chose: Served was never answered, Again is a skip, and a Banished band is
    /// one you threw out in 45 seconds — being asked its name would test nothing about you. Here the
    /// player has 4 summons and 9 non-summons; if any of the others leaked in, the count would not be 4.
    /// </summary>
    [SkippableFact]
    public async Task Pool_IsSummonsOnly_NeverBanishedServedOrAgain()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 4, banished: 3, served: 3, again: 3);

        using HttpClient client = await SignInAsync(factory, meId);

        Availability? availability = await client.GetFromJsonAsync<Availability>(
            "/api/games/guess/availability?difficulty=normal");

        Assert.NotNull(availability);
        Assert.Equal(4, availability!.SummonsAvailable);
        Assert.True(availability.Playable);

        // And the deal agrees with the count: every round is a band this player summoned.
        GuessGame? game = await StartAsync(client, "normal");

        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        HashSet<Guid> summoned = (await db.Rites
                .Where(r => r.UserId == meId && r.State == RiteState.Summoned)
                .Select(r => r.ArtistId)
                .ToListAsync())
            .ToHashSet();

        List<GameRound> rounds = await db.GameRounds.Where(r => r.GameId == game!.Id).ToListAsync();

        Assert.NotEmpty(rounds);
        Assert.All(rounds, r => Assert.Contains(r.ArtistId, summoned));
    }

    /// <summary>
    /// The game is played over YOUR grimoire — a friend's summons are not your pool, however many they
    /// have. This is the difference from the verdict game, where the opponent IS the subject, and it is
    /// why this game needs no consent: nothing of theirs is read.
    /// </summary>
    [SkippableFact]
    public async Task Pool_IsMineAlone_AndAFriendsSummonsDoNotFillIt()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid friendId = await CreateUserAsync(factory);
        Guid meId = await CreateUserAsync(factory);
        await BefriendAsync(factory, meId, friendId);

        // They have played plenty. I have played nothing.
        await GiveRitesAsync(factory, friendId, summoned: 20, banished: 5, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        Availability? availability = await client.GetFromJsonAsync<Availability>(
            "/api/games/guess/availability?difficulty=normal");

        Assert.Equal(0, availability!.SummonsAvailable);
        Assert.False(availability.Playable);
        Assert.Equal("too-few-summons", availability.Reason);

        // Not even by challenging them: a challenge sends a score, it does not borrow a grimoire.
        HttpResponseMessage start = await client.PostAsJsonAsync(
            "/api/games/guess", new { difficulty = "normal", opponentId = friendId });

        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
    }

    /// <summary>An almost-empty grimoire says so plainly, and says which fact stops it.</summary>
    [SkippableFact]
    public async Task Availability_ReportsTooFewSummons_OnAnAlmostEmptyGrimoire()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 2, banished: 8, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        Availability? availability = await client.GetFromJsonAsync<Availability>(
            "/api/games/guess/availability?difficulty=hard");

        Assert.False(availability!.Playable);
        Assert.Equal("too-few-summons", availability.Reason);

        HttpResponseMessage start = await client.PostAsJsonAsync("/api/games/guess", new { difficulty = "hard" });
        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
    }

    /// <summary>
    /// The per-difficulty empty state. Three summons cannot fill a four-name multiple choice, but they
    /// can be typed — so Normal refuses with its own reason and Hard deals. Two different facts, two
    /// different sentences; a greyed-out button would have said neither.
    /// </summary>
    [SkippableFact]
    public async Task ThreeSummons_BlockTheMultipleChoice_ButDealHard()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 3, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        Availability? normal = await client.GetFromJsonAsync<Availability>(
            "/api/games/guess/availability?difficulty=normal");

        Assert.False(normal!.Playable);
        Assert.Equal("not-enough-choices", normal.Reason);

        Availability? hard = await client.GetFromJsonAsync<Availability>(
            "/api/games/guess/availability?difficulty=hard");

        Assert.True(hard!.Playable);

        HttpResponseMessage refused = await client.PostAsJsonAsync("/api/games/guess", new { difficulty = "normal" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        GuessGame? game = await StartAsync(client, "hard");
        Assert.Equal(3, game!.Rounds.Count);
    }

    // -----------------------------------------------------------------------
    // Blind: the game cannot be won by reading the response
    // -----------------------------------------------------------------------

    /// <summary>
    /// The anti-cheat check over the wire for Hard, where NOTHING about the band may go out: no name,
    /// no id, no summary. Read as the raw body, because a typed DTO would hide a leaking field behind a
    /// property it does not map — and the leak would be a 200 of exactly the right shape, invisible to
    /// any test that only reads status codes.
    /// </summary>
    [SkippableFact]
    public async Task Hard_ServesEveryRoundBlind_AndTheRawBodyNamesNothing()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/games/guess", new { difficulty = "hard" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        // No band name anywhere in the payload...
        Assert.DoesNotContain("Guess Band", body, StringComparison.OrdinalIgnoreCase);

        // ...and no artist id either. In this game the id IS the answer: one lookup and the round is over.
        foreach (Guid artistId in await RoundArtistIdsAsync(factory, (await response.Content.ReadFromJsonAsync<GuessGame>())!.Id))
        {
            Assert.DoesNotContain(artistId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        }

        GuessGame? game = await client.GetFromJsonAsync<GuessGame>(
            $"/api/games/guess/{(await ParseIdAsync(body))}");

        Assert.All(game!.Rounds, r =>
        {
            Assert.Null(r.Artist);
            Assert.Null(r.Correct);
            // Hard offers nothing to pick from: null, not an empty list — they mean different things.
            Assert.Null(r.Choices);
            Assert.Contains("/audio", r.AudioUrl);
        });
    }

    /// <summary>
    /// Normal's contract is subtler, because four names DO go out and one of them is true. What must
    /// not go out is which. So: the round carries no artist, no correctness — and the answer's id
    /// appears in the body exactly ONCE, as one choice among four, never a second time in a field that
    /// would give it away.
    /// </summary>
    [SkippableFact]
    public async Task Normal_ServesFourNames_AndTheRawBodyNeverSaysWhichIsTrue()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 8, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/games/guess", new { difficulty = "normal" });
        string body = await response.Content.ReadAsStringAsync();

        GuessGame? game = await response.Content.ReadFromJsonAsync<GuessGame>();
        Assert.NotNull(game);

        Dictionary<Guid, Guid> answers = await RoundAnswersAsync(factory, game!.Id);

        Assert.All(game.Rounds, r =>
        {
            Assert.Null(r.Artist);
            Assert.Null(r.Correct);
            Assert.NotNull(r.Choices);
            Assert.Equal(4, r.Choices!.Count);

            // The answer is on the list — a round with no right button is not a round...
            Guid answer = answers[r.Token];
            Assert.Contains(r.Choices, c => c.ArtistId == answer);

            // ...and it is on it once, and the four are four different bands.
            Assert.Equal(4, r.Choices.Select(c => c.ArtistId).Distinct().Count());
        });

        // The decisive one: the answer's id occurs in the raw body exactly as many times as it appears
        // among the choices, and no more. A stray artistId on the round would show up as one extra.
        foreach (RoundDto round in game.Rounds)
        {
            Guid answer = answers[round.Token];
            int inChoices = game.Rounds.Sum(r => r.Choices!.Count(c => c.ArtistId == answer));

            Assert.Equal(inChoices, Occurrences(body, answer.ToString()));
        }
    }

    /// <summary>
    /// The answer's POSITION must carry no information. If it always sat first — or merely favoured a
    /// slot — the game would be won by pressing that button, silently, for ever. Dealt across many
    /// games so that a fixed or lopsided position cannot hide behind one lucky draw.
    /// </summary>
    [SkippableFact]
    public async Task Normal_TheAnswer_DoesNotSitInAFixedPosition()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 10, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        HashSet<int> positions = [];
        int rounds = 0;

        for (int i = 0; i < 6; i++)
        {
            GuessGame? game = await StartAsync(client, "normal");
            Dictionary<Guid, Guid> answers = await RoundAnswersAsync(factory, game!.Id);

            foreach (RoundDto round in game.Rounds)
            {
                Guid answer = answers[round.Token];
                positions.Add(round.Choices!.Select((c, index) => (c, index)).First(x => x.c.ArtistId == answer).index);
                rounds++;
            }
        }

        Assert.True(rounds >= 15, $"Not enough rounds to judge the spread ({rounds}).");
        Assert.True(
            positions.Count > 1,
            $"Across {rounds} rounds the answer only ever landed in position(s) {string.Join(",", positions)}.");
    }

    /// <summary>
    /// The choices are NOT stored — there is no column for them — so every read recomputes them. That
    /// makes stability a security property, not a nicety: if two reads could disagree, a player would
    /// reload the game twice and take the name that appears in both draws. It is the answer.
    /// </summary>
    [SkippableFact]
    public async Task Normal_Choices_AreIdentical_OnEveryReadOfTheRound()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 10, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        GuessGame? dealt = await StartAsync(client, "normal");
        GuessGame? first = await client.GetFromJsonAsync<GuessGame>($"/api/games/guess/{dealt!.Id}");
        GuessGame? second = await client.GetFromJsonAsync<GuessGame>($"/api/games/guess/{dealt.Id}");

        Assert.Equal(Fingerprint(dealt), Fingerprint(first!));
        Assert.Equal(Fingerprint(first!), Fingerprint(second!));
    }

    /// <summary>
    /// The other half of that guarantee, and the one that is easy to miss: the choices must not move
    /// when the GRIMOIRE moves. A player summoning a band in another tab mid-game must not reshape a
    /// live round's decoys — or the same intersection attack works, just with a slower first step. The
    /// pool is frozen to the rites resolved at or before the deal, and this is what proves it.
    /// </summary>
    [SkippableFact]
    public async Task Normal_Choices_DoNotShift_WhenTheGrimoireGrowsMidGame()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        GuessGame? dealt = await StartAsync(client, "normal");
        string before = Fingerprint(dealt!);

        // The Rite keeps being played while the game sits open: ten new summons resolved NOW — after
        // the deal — several of them necessarily nearer some round's band than the decoys it has.
        await GiveRitesAsync(
            factory, meId, summoned: 10, banished: 0, served: 0, again: 0, resolvedAt: DateTimeOffset.UtcNow);

        GuessGame? after = await client.GetFromJsonAsync<GuessGame>($"/api/games/guess/{dealt!.Id}");

        Assert.Equal(before, Fingerprint(after!));
    }

    /// <summary>
    /// Where the decoys come from: the player's own grimoire, and only it. A decoy from outside would
    /// be a band they never summoned — and once a player noticed that, every round would be solvable by
    /// elimination against their own memory of what they had played.
    /// </summary>
    [SkippableFact]
    public async Task Normal_EveryChoice_ComesFromThePlayersOwnGrimoire()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid strangerId = await CreateUserAsync(factory);
        Guid meId = await CreateUserAsync(factory);

        // A big foreign grimoire, and bands nobody summoned at all, both sitting in the same catalogue.
        await GiveRitesAsync(factory, strangerId, summoned: 15, banished: 0, served: 0, again: 0);
        await GiveRitesAsync(factory, meId, summoned: 8, banished: 4, served: 2, again: 2);

        using HttpClient client = await SignInAsync(factory, meId);

        GuessGame? game = await StartAsync(client, "normal");

        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        HashSet<Guid> mySummons = (await db.Rites
                .Where(r => r.UserId == meId && r.State == RiteState.Summoned)
                .Select(r => r.ArtistId)
                .ToListAsync())
            .ToHashSet();

        Assert.All(game!.Rounds, r => Assert.All(r.Choices!, c => Assert.Contains(c.ArtistId, mySummons)));
    }

    /// <summary>A game belongs to the player dealt it — a challenged friend cannot read it, only its score.</summary>
    [SkippableFact]
    public async Task Game_IsNotReadable_ByTheChallengedFriend()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid friendId = await CreateUserAsync(factory);
        Guid meId = await CreateUserAsync(factory);
        await BefriendAsync(factory, meId, friendId);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "normal", friendId);

        using HttpClient theirs = await SignInAsync(factory, friendId);
        HttpResponseMessage response = await theirs.GetAsync($"/api/games/guess/{game!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Answering: Normal
    // -----------------------------------------------------------------------

    /// <summary>
    /// A whole game played right, by reading each round's band from the database and picking it.
    /// Scores full, and at Normal's rate: one point a round.
    /// </summary>
    [SkippableFact]
    public async Task Normal_AnsweringEveryRoundCorrectly_ScoresFull_AtOnePointARound()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 8, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "normal");

        Dictionary<Guid, Guid> answers = await RoundAnswersAsync(factory, game!.Id);

        AnswerResult? last = null;

        foreach (RoundDto round in game.Rounds)
        {
            last = await AnswerAsync(client, round.Token, new { artistId = answers[round.Token] });
            Assert.True(last!.Correct);
            Assert.NotNull(last.Reveal);
        }

        Assert.True(last!.Finished);
        Assert.Equal(game.Rounds.Count, last.Score.Correct);
        Assert.Equal(game.Rounds.Count, last.Score.Points);
        Assert.Equal(1, last.Score.PointsPerRound);
    }

    /// <summary>Picking the wrong name scores zero — and the band is still revealed, so the round teaches something.</summary>
    [SkippableFact]
    public async Task Normal_PickingAWrongChoice_IsWrong_ButStillReveals()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 8, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "normal");

        Dictionary<Guid, Guid> answers = await RoundAnswersAsync(factory, game!.Id);
        RoundDto round = game.Rounds[0];
        Guid wrong = round.Choices!.First(c => c.ArtistId != answers[round.Token]).ArtistId;

        AnswerResult? result = await AnswerAsync(client, round.Token, new { artistId = wrong });

        Assert.False(result!.Correct);
        Assert.NotNull(result.Reveal);
        Assert.Equal(0, result.Score.Correct);
        Assert.Equal(0, result.Score.Points);
    }

    /// <summary>
    /// An id from outside the four offered is a broken client, not a wrong guess, and the two must not
    /// score the same — 400, and the round stays open for a real answer.
    /// </summary>
    [SkippableFact]
    public async Task Normal_AnIdOutsideTheChoices_IsRejected_AndDoesNotBurnTheRound()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 8, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "normal");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/games/guess/rounds/{game!.Rounds[0].Token}/answer", new { artistId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Still answerable: a rejected body is not an answer.
        Dictionary<Guid, Guid> answers = await RoundAnswersAsync(factory, game.Id);
        AnswerResult? real = await AnswerAsync(client, game.Rounds[0].Token, new { artistId = answers[game.Rounds[0].Token] });
        Assert.True(real!.Correct);
    }

    /// <summary>A multiple choice needs a choice. Typing at it is not an answer here.</summary>
    [SkippableFact]
    public async Task Normal_RejectsATypedName()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 8, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "normal");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/games/guess/rounds/{game!.Rounds[0].Token}/answer", new { name = "Darkthrone" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Answering: Hard
    // -----------------------------------------------------------------------

    /// <summary>
    /// The typed game, played right, and priced right: three points a round, because four names on
    /// screen hand a player one round in four for free and a blank field hands them nothing.
    /// </summary>
    [SkippableFact]
    public async Task Hard_TypingEveryNameCorrectly_ScoresFull_AtThreePointsARound()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "hard");

        Dictionary<Guid, string> names = await RoundNamesAsync(factory, game!.Id);

        AnswerResult? last = null;

        foreach (RoundDto round in game.Rounds)
        {
            last = await AnswerAsync(client, round.Token, new { name = names[round.Token] });
            Assert.True(last!.Correct);
        }

        Assert.True(last!.Finished);
        Assert.Equal(game.Rounds.Count, last.Score.Correct);
        Assert.Equal(game.Rounds.Count * 3, last.Score.Points);
        Assert.Equal(3, last.Score.PointsPerRound);
    }

    /// <summary>
    /// The generosity, end to end and against a real band name: case and spacing folded away, and a
    /// single slip forgiven. Failing somebody over that would make Hard a typing test.
    /// </summary>
    [SkippableFact]
    public async Task Hard_ForgivesCase_Spacing_AndASingleTypo()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        Guid artistId = await GiveNamedSummonAsync(factory, meId, "Darkthrone");
        await GiveNamedSummonAsync(factory, meId, "Burzum");
        await GiveNamedSummonAsync(factory, meId, "Emperor");

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "hard");

        RoundDto round = await RoundForArtistAsync(factory, game!, artistId);

        AnswerResult? result = await AnswerAsync(client, round.Token, new { name = "  DARKTHRON " });

        Assert.True(result!.Correct);
    }

    /// <summary>
    /// The limit of that generosity, and the one that must never move: typing the name of a DIFFERENT
    /// band in the player's own grimoire is wrong, not "close". A judge loose enough to accept this
    /// would have stopped measuring anything.
    /// </summary>
    [SkippableFact]
    public async Task Hard_TypingAnotherBandFromMyOwnGrimoire_IsWrong()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        Guid artistId = await GiveNamedSummonAsync(factory, meId, "Immortal");
        await GiveNamedSummonAsync(factory, meId, "Immortals");
        await GiveNamedSummonAsync(factory, meId, "Emperor");

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "hard");

        RoundDto round = await RoundForArtistAsync(factory, game!, artistId);

        // One edit from the answer — well within the budget — and yet a real, different band that this
        // very player has summoned. The name wins over the distance.
        AnswerResult? result = await AnswerAsync(client, round.Token, new { name = "Immortals" });

        Assert.False(result!.Correct);
    }

    /// <summary>
    /// Hard has no choices, so there is no id to legitimately hold — accepting one would be the easy
    /// mode with the answers hidden, paid at triple. 400.
    /// </summary>
    [SkippableFact]
    public async Task Hard_RejectsAnArtistIdAnswer()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "hard");

        Dictionary<Guid, Guid> answers = await RoundAnswersAsync(factory, game!.Id);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/games/guess/rounds/{game.Rounds[0].Token}/answer",
            new { artistId = answers[game.Rounds[0].Token] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>An empty guess is not an answer, and does not burn the round either.</summary>
    [SkippableFact]
    public async Task Hard_RejectsAnEmptyGuess()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "hard");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/games/guess/rounds/{game!.Rounds[0].Token}/answer", new { name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // The rules every round obeys
    // -----------------------------------------------------------------------

    /// <summary>
    /// A round is answered once. Without this the reveal makes the retry free: answer, read the band
    /// off the reveal, answer again correctly. The 409 is what keeps the score meaning anything.
    /// </summary>
    [SkippableFact]
    public async Task AnsweringARoundTwice_IsRejected()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 8, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "normal");

        Dictionary<Guid, Guid> answers = await RoundAnswersAsync(factory, game!.Id);
        RoundDto round = game.Rounds[0];

        // First a deliberate miss, so the reveal hands over the right answer...
        await AnswerAsync(client, round.Token, new { artistId = round.Choices!.First(c => c.ArtistId != answers[round.Token]).ArtistId });

        // ...and then the "correction" that must not be allowed to land.
        HttpResponseMessage again = await client.PostAsJsonAsync(
            $"/api/games/guess/rounds/{round.Token}/answer", new { artistId = answers[round.Token] });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        GuessGame? resumed = await client.GetFromJsonAsync<GuessGame>($"/api/games/guess/{game.Id}");
        Assert.Equal(0, resumed!.Score.Correct);
    }

    /// <summary>Somebody else's round is not answerable — and a 404 does not even confirm it exists.</summary>
    [SkippableFact]
    public async Task AnsweringSomeoneElsesRound_IsRejected()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        Guid strangerId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "normal");

        using HttpClient stranger = await SignInAsync(factory, strangerId);
        HttpResponseMessage response = await stranger.PostAsJsonAsync(
            $"/api/games/guess/rounds/{game!.Rounds[0].Token}/answer",
            new { artistId = game.Rounds[0].Choices![0].ArtistId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>An unknown difficulty is refused, never defaulted: dealing Normal to somebody who asked
    /// for Hard would misprice their whole game.</summary>
    [SkippableFact]
    public async Task AnUnknownDifficulty_IsRefused()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        HttpResponseMessage start = await client.PostAsJsonAsync("/api/games/guess", new { difficulty = "impossible" });
        Assert.Equal(HttpStatusCode.BadRequest, start.StatusCode);

        HttpResponseMessage availability = await client.GetAsync("/api/games/guess/availability?difficulty=impossible");
        Assert.Equal(HttpStatusCode.BadRequest, availability.StatusCode);
    }

    /// <summary>
    /// The audio endpoint is kind-scoped: a verdict game's round token is not redeemable here. The two
    /// games choose their clip by different rules, and a token that crossed between them would quietly
    /// serve the wrong one.
    /// </summary>
    [SkippableFact]
    public async Task Audio_RefusesARoundTokenFromTheOtherGame()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, verdictOptIn: true);
        Guid meId = await CreateUserAsync(factory);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        HttpResponseMessage started = await client.PostAsJsonAsync("/api/games/verdict", new { opponentId });
        started.EnsureSuccessStatusCode();

        using JsonDocument doc = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        string token = doc.RootElement.GetProperty("rounds")[0].GetProperty("token").GetString()!;

        HttpResponseMessage audio = await client.GetAsync($"/api/games/guess/rounds/{token}/audio");

        Assert.Equal(HttpStatusCode.NotFound, audio.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Solo, and the turn
    // -----------------------------------------------------------------------

    /// <summary>
    /// The solo game: no opponent, no inbox line, nobody told. It falls out of a nullable column — no
    /// table, no second code path — which is exactly what D66 left the column nullable for.
    /// </summary>
    [SkippableFact]
    public async Task Solo_IsPlayable_AndTellsNobody()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "normal");

        Assert.Null(game!.OpponentId);

        Dictionary<Guid, Guid> answers = await RoundAnswersAsync(factory, game.Id);

        foreach (RoundDto round in game.Rounds)
        {
            await AnswerAsync(client, round.Token, new { artistId = answers[round.Token] });
        }

        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        Assert.Equal(GameStatus.Finished, (await db.Games.FirstAsync(g => g.Id == game.Id)).Status);
        Assert.Empty(await db.Notifications.Where(n => n.Type == NotificationType.GuessGamePlayed).ToListAsync());
    }

    /// <summary>
    /// The turn hand-off (D60): finishing a challenged game drops the score in the friend's inbox, and
    /// that line IS the invitation. Nothing realtime, nothing waiting, no socket.
    /// </summary>
    [SkippableFact]
    public async Task AChallengedGame_DropsTheScoreInTheFriendsInbox()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid friendId = await CreateUserAsync(factory);
        Guid meId = await CreateUserAsync(factory);
        await BefriendAsync(factory, meId, friendId);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GuessGame? game = await StartAsync(client, "hard", friendId);

        Assert.Equal(friendId, game!.OpponentId);

        Dictionary<Guid, string> names = await RoundNamesAsync(factory, game.Id);

        foreach (RoundDto round in game.Rounds)
        {
            await AnswerAsync(client, round.Token, new { name = names[round.Token] });
        }

        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        Notification notification = await db.Notifications
            .SingleAsync(n => n.UserId == friendId && n.Type == NotificationType.GuessGamePlayed);

        Assert.Equal(meId, notification.ActorId);
        Assert.Contains(game.Rounds.Count.ToString(), notification.PayloadJson);
    }

    /// <summary>A stranger cannot be challenged: a score in somebody's inbox needs a friendship first.</summary>
    [SkippableFact]
    public async Task ChallengingAStranger_IsForbidden()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid strangerId = await CreateUserAsync(factory);
        Guid meId = await CreateUserAsync(factory);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        HttpResponseMessage start = await client.PostAsJsonAsync(
            "/api/games/guess", new { difficulty = "normal", opponentId = strangerId });

        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
    }

    /// <summary>A pending request is not a friendship — the addressee never agreed to anything.</summary>
    [SkippableFact]
    public async Task ChallengingAMerelyPendingFriend_IsForbidden()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid friendId = await CreateUserAsync(factory);
        Guid meId = await CreateUserAsync(factory);
        await BefriendAsync(factory, meId, friendId, FriendshipStatus.Pending);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        HttpResponseMessage start = await client.PostAsJsonAsync(
            "/api/games/guess", new { difficulty = "normal", opponentId = friendId });

        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
    }

    /// <summary>
    /// The history carries both sides of the turn — the games you played and the challenges sent to
    /// you — because that list is where two scores over two different grimoires finally meet. It also
    /// carries the difficulty, without which comparing them would be meaningless.
    /// </summary>
    [SkippableFact]
    public async Task Games_ListBothSidesOfTheTurn_WithTheirDifficulty()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid friendId = await CreateUserAsync(factory);
        Guid meId = await CreateUserAsync(factory);
        await BefriendAsync(factory, meId, friendId);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);
        await GiveRitesAsync(factory, friendId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient mine = await SignInAsync(factory, meId);
        await StartAsync(mine, "hard", friendId);

        using HttpClient theirs = await SignInAsync(factory, friendId);
        await StartAsync(theirs, "normal", meId);

        List<GameSummary>? list = await mine.GetFromJsonAsync<List<GameSummary>>("/api/games/guess");

        Assert.Equal(2, list!.Count);
        Assert.Contains(list, g => g.PlayedByMe && g.OtherUserId == friendId && g.Difficulty == "Hard");
        Assert.Contains(list, g => !g.PlayedByMe && g.OtherUserId == friendId && g.Difficulty == "Normal");
    }

    /// <summary>
    /// The two games share one pair of tables, so the discriminator has to be ENFORCED and not merely
    /// stored. A guess game read through the verdict game's endpoint would come back shaped as a thing
    /// it is not — same rows, wrong contract — and its audio token redeemed there would serve the
    /// Rite's own cut instead of the unheard one this game exists to play.
    /// </summary>
    [SkippableFact]
    public async Task AGuessGame_IsNotReadable_ThroughTheVerdictGamesEndpoints()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid friendId = await CreateUserAsync(factory);
        Guid meId = await CreateUserAsync(factory);
        await BefriendAsync(factory, meId, friendId);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        // A CHALLENGED game: it has an opponent, so it is the one shaped most like a verdict game.
        GuessGame? game = await StartAsync(client, "normal", friendId);

        HttpResponseMessage asVerdict = await client.GetAsync($"/api/games/verdict/{game!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, asVerdict.StatusCode);

        HttpResponseMessage audio = await client.GetAsync($"/api/games/rounds/{game.Rounds[0].Token}/audio");
        Assert.Equal(HttpStatusCode.NotFound, audio.StatusCode);
    }

    /// <summary>The two games do not bleed into each other's lists: each kind reads only its own rows.</summary>
    [SkippableFact]
    public async Task TheHistory_DoesNotMixTheTwoGames()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, verdictOptIn: true);
        Guid meId = await CreateUserAsync(factory);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);
        await GiveRitesAsync(factory, meId, summoned: 6, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        (await client.PostAsJsonAsync("/api/games/verdict", new { opponentId })).EnsureSuccessStatusCode();
        await StartAsync(client, "normal");

        List<GameSummary>? guessGames = await client.GetFromJsonAsync<List<GameSummary>>("/api/games/guess");
        Assert.Single(guessGames!);

        HttpResponseMessage verdictList = await client.GetAsync("/api/games/verdict");
        using JsonDocument doc = JsonDocument.Parse(await verdictList.Content.ReadAsStringAsync());
        Assert.Equal(1, doc.RootElement.GetArrayLength());
    }

    // -- helpers --------------------------------------------------------------

    private WebApplicationFactory<Program> Factory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                ServiceDescriptor? optionsDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<GrimoireDbContext>));

                if (optionsDescriptor is not null)
                {
                    services.Remove(optionsDescriptor);
                }

                services.AddDbContext<GrimoireDbContext>(options =>
                    options.UseNpgsql(TestConnectionString, npgsql => npgsql.UseVector())
                        .UseSnakeCaseNamingConvention());
            });
        });
    }

    /// <summary>Registers a user through the real auth endpoint.</summary>
    private static async Task<Guid> CreateUserAsync(WebApplicationFactory<Program> factory, bool? verdictOptIn = null)
    {
        using HttpClient client = factory.CreateClient();

        string email = $"guess-{Guid.NewGuid():N}@example.com";
        HttpResponseMessage register = await client.PostAsJsonAsync(
            "/api/auth/register", new { email, password = "Passw0rd!23" });

        register.EnsureSuccessStatusCode();

        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        GrimoireUser user = await db.Users.FirstAsync(u => u.Email == email);
        user.Handle = $"h{Guid.NewGuid():N}"[..12];
        user.VerdictGameOptIn = verdictOptIn;
        await db.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>A client carrying a fresh access token for an existing user.</summary>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, Guid userId)
    {
        string email;

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
            email = (await db.Users.FirstAsync(u => u.Id == userId)).Email!;
        }

        HttpClient client = factory.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login", new { email, password = "Passw0rd!23" });

        login.EnsureSuccessStatusCode();

        AuthTokens? tokens = await login.Content.ReadFromJsonAsync<AuthTokens>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return client;
    }

    private static async Task BefriendAsync(
        WebApplicationFactory<Program> factory,
        Guid a,
        Guid b,
        FriendshipStatus status = FriendshipStatus.Accepted)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        db.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = a,
            AddresseeId = b,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Gives a user rites in each state. Every band gets a cached, allow-listed preview so the
    /// just-in-time probe short-circuits on the cache — these tests make no network call — and a
    /// distinct embedding, so the nearest-neighbour decoys have a real map to be near in.
    ///
    /// <para>
    /// <paramref name="resolvedAt"/> is not decoration: the decoy pool is frozen to the rites resolved
    /// at or before a game was dealt, so WHEN a rite was resolved is the difference between a band that
    /// belongs in a live game's choices and one that must never touch them. It defaults to a minute ago
    /// — "this was already in the grimoire" — and a test about summoning mid-game passes <c>UtcNow</c>,
    /// which is what The Rite actually stamps.
    /// </para>
    /// </summary>
    private static async Task GiveRitesAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        int summoned,
        int banished,
        int served,
        int again,
        DateTimeOffset? resolvedAt = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        DateTimeOffset stamp = resolvedAt ?? DateTimeOffset.UtcNow.AddMinutes(-1);

        await AddRitesAsync(db, userId, RiteState.Summoned, summoned, stamp);
        await AddRitesAsync(db, userId, RiteState.Banished, banished, stamp);
        await AddRitesAsync(db, userId, RiteState.Served, served, stamp);
        await AddRitesAsync(db, userId, RiteState.Again, again, stamp);

        await db.SaveChangesAsync();
    }

    private static async Task AddRitesAsync(
        GrimoireDbContext db,
        Guid userId,
        RiteState state,
        int count,
        DateTimeOffset resolvedAt)
    {
        for (int i = 0; i < count; i++)
        {
            Artist artist = NewArtist($"Guess Band {state} {i} {Guid.NewGuid():N}"[..28]);

            db.Artists.Add(artist);
            db.Rites.Add(new Rite
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ArtistId = artist.Id,
                State = state,
                ServedAt = DateTimeOffset.UtcNow.AddDays(-1),
                // A Served rite has not been resolved: that is what Served means.
                ResolvedAt = state == RiteState.Served ? null : resolvedAt,
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>One summon with a name we choose — for the cases that are about the name itself.</summary>
    private static async Task<Guid> GiveNamedSummonAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        string name)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        Artist artist = NewArtist(name);

        db.Artists.Add(artist);
        db.Rites.Add(new Rite
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ArtistId = artist.Id,
            State = RiteState.Summoned,
            ServedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ResolvedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        await db.SaveChangesAsync();

        return artist.Id;
    }

    private static Artist NewArtist(string name)
    {
        float[] vector = new float[768];
        Array.Fill(vector, 0.1f);

        // A distinct first component: the bands sit in slightly different places, so "nearest" is a
        // real question rather than a tie broken by id.
        vector[0] = Random.Shared.NextSingle();

        return new Artist
        {
            Id = Guid.NewGuid(),
            Mbid = Guid.NewGuid(),
            Name = name,
            SortName = name,
            Kind = ArtistKind.Group,
            Country = "XX",
            FormedYear = 1990,
            Tags = ["guess-test"],
            Embedding = new Vector(vector),
            PreviewUrl = AllowlistedPreview,
        };
    }

    private static async Task<GuessGame?> StartAsync(HttpClient client, string difficulty, Guid? opponentId = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/games/guess", new { difficulty, opponentId });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<GuessGame>();
    }

    private static async Task<AnswerResult?> AnswerAsync(HttpClient client, Guid token, object body)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/games/guess/rounds/{token}/answer", body);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AnswerResult>();
    }

    /// <summary>Each round's real band, straight from the database — the tests' oracle.</summary>
    private static async Task<Dictionary<Guid, Guid>> RoundAnswersAsync(
        WebApplicationFactory<Program> factory,
        Guid gameId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        return await db.GameRounds
            .Where(r => r.GameId == gameId)
            .ToDictionaryAsync(r => r.Id, r => r.ArtistId);
    }

    /// <summary>Each round's real band NAME, for the typed mode.</summary>
    private static async Task<Dictionary<Guid, string>> RoundNamesAsync(
        WebApplicationFactory<Program> factory,
        Guid gameId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        return await db.GameRounds
            .Where(r => r.GameId == gameId)
            .Join(db.Artists, r => r.ArtistId, a => a.Id, (r, a) => new { r.Id, a.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name);
    }

    private static async Task<List<Guid>> RoundArtistIdsAsync(WebApplicationFactory<Program> factory, Guid gameId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        return await db.GameRounds.Where(r => r.GameId == gameId).Select(r => r.ArtistId).ToListAsync();
    }

    /// <summary>The round that happens to be about a given band, for the name cases.</summary>
    private static async Task<RoundDto> RoundForArtistAsync(
        WebApplicationFactory<Program> factory,
        GuessGame game,
        Guid artistId)
    {
        Dictionary<Guid, Guid> answers = await RoundAnswersAsync(factory, game.Id);

        return game.Rounds.Single(r => answers[r.Token] == artistId);
    }

    /// <summary>Everything a resumed game must reproduce exactly: the rounds, in order, with their choices in order.</summary>
    private static string Fingerprint(GuessGame game)
    {
        return string.Join(
            "|",
            game.Rounds
                .OrderBy(r => r.Ordinal)
                .Select(r => $"{r.Ordinal}:{string.Join(",", (r.Choices ?? []).Select(c => c.ArtistId))}"));
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;

        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static async Task<Guid> ParseIdAsync(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);

        return await Task.FromResult(doc.RootElement.GetProperty("id").GetGuid());
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record Availability(bool Playable, string? Reason, int SummonsAvailable);

    private sealed record ChoiceDto(Guid ArtistId, string Name);

    private sealed record RoundDto(
        Guid Token,
        int Ordinal,
        string AudioUrl,
        IReadOnlyList<ChoiceDto>? Choices,
        ArtistSummary? Artist,
        bool? Correct);

    private sealed record ArtistSummary(Guid Id, string Name);

    private sealed record GuessGame(
        Guid Id,
        string Difficulty,
        Guid? OpponentId,
        string? OpponentHandle,
        string Status,
        IReadOnlyList<RoundDto> Rounds,
        Score Score);

    private sealed record Score(int Correct, int Answered, int Total, int Points, int PointsPerRound);

    private sealed record AnswerResult(bool Correct, ArtistDetail? Reveal, Score Score, bool Finished);

    private sealed record ArtistDetail(Guid Id, string Name);

    private sealed record GameSummary(
        Guid Id,
        bool PlayedByMe,
        string Difficulty,
        Guid? OtherUserId,
        string? OtherHandle,
        string Status);

    private sealed record AuthTokens(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt);

    /// <summary>
    /// The splice between the two halves of this wave (D67): the harvest stores alternate clips, and
    /// <see cref="RiteClipSource"/> must actually play one. This is the test the whole wave reduces to
    /// — on your own grimoire you have, by definition, already heard <c>preview_url</c>, so a round
    /// built on it asks whether you remember 45 seconds of audio rather than whether you know the band.
    /// <para>
    /// It bites at the seam most likely to rot silently: <c>Previews</c> is a lazy navigation, and an
    /// unloaded collection is <b>empty, not absent</b>. Drop the <c>LoadAsync</c> and every round falls
    /// back to the heard cut — no error, no failing build, the wave simply undone in the dark.
    /// </para>
    /// </summary>
    [SkippableFact]
    public async Task ClipSource_PlaysAnAlternateTrackWhenTheHarvestFoundOne()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        const string alternate = "https://audio-ssl.itunes.apple.com/preview/alternate.m4a";
        Guid artistId;

        using (IServiceScope seed = factory.Services.CreateScope())
        {
            GrimoireDbContext db = seed.ServiceProvider.GetRequiredService<GrimoireDbContext>();

            Artist artist = NewArtist("Splice Band");
            artistId = artist.Id;
            db.Artists.Add(artist);
            db.ArtistPreviews.Add(new ArtistPreview
            {
                ArtistId = artist.Id,
                Url = alternate,
                Source = "iTunes",
                TrackTitle = "A Second Song",
                CollectedAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
        }

        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext context = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
        IGuessPreviewSource clips = scope.ServiceProvider.GetRequiredService<IGuessPreviewSource>();

        Artist tracked = await context.Artists.FirstAsync(a => a.Id == artistId);
        string heard = tracked.PreviewUrl!;

        GuessClip? clip = await clips.ChooseAsync(tracked, heard, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(clip);
        Assert.Equal(alternate, clip!.Url);
        Assert.NotEqual(heard, clip.Url);
        Assert.True(clip.IsDifferentTrack, "A band with a harvested alternate must not replay the heard cut.");
    }

    /// <summary>
    /// The other half of the same seam, and the reason the splice is a fallback rather than a filter:
    /// most of the catalogue was resolved just-in-time long before the alternates table existed, so it
    /// has exactly one clip. Those bands must still be playable — degraded and truthfully labelled, not
    /// dropped (Invariant 5).
    /// </summary>
    [SkippableFact]
    public async Task ClipSource_ReplaysTheHeardCutHonestlyWhenTheBandHasNoAlternate()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid artistId;

        using (IServiceScope seed = factory.Services.CreateScope())
        {
            GrimoireDbContext db = seed.ServiceProvider.GetRequiredService<GrimoireDbContext>();

            Artist artist = NewArtist("Unharvested Band");
            artistId = artist.Id;
            db.Artists.Add(artist);

            await db.SaveChangesAsync();
        }

        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext context = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
        IGuessPreviewSource clips = scope.ServiceProvider.GetRequiredService<IGuessPreviewSource>();

        Artist tracked = await context.Artists.FirstAsync(a => a.Id == artistId);
        string heard = tracked.PreviewUrl!;

        GuessClip? clip = await clips.ChooseAsync(tracked, heard, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(clip);
        Assert.Equal(heard, clip!.Url);
        Assert.False(clip.IsDifferentTrack, "Replaying the heard cut must never be reported as a new track.");
    }
}
