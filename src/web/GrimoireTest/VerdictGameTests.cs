using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
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
/// The verdict game end-to-end against a real PostgreSQL and real HTTP: the pool, the two gates
/// (accepted friendship and the opponent's opt-in), the blind serve, the scoring and the designed
/// empty states. Uses a throwaway database it creates and drops, never the development one. No
/// network beyond the API: every fixture band is inserted with a cached, allow-listed preview URL, so
/// the just-in-time resolver short-circuits on the cache and never reaches iTunes. Skipped cleanly
/// when PostgreSQL is down.
/// </summary>
public class VerdictGameTests : IAsyncLifetime
{
    private const string MaintenanceConnectionString =
        "Host=localhost;Port=5433;Database=grimoire;Username=grimoire;Password=grimoire;Timeout=3;Command Timeout=5";

    private const string AllowlistedPreview = "https://audio-ssl.itunes.apple.com/preview/fixture.m4a";

    private readonly string _databaseName = $"grimoire_test_games_{Guid.NewGuid():N}";

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
    // The pool: only resolved rites are verdicts
    // -----------------------------------------------------------------------

    /// <summary>
    /// THE pool rule. A friend with plenty of rites but only two RESOLVED ones cannot make a game:
    /// Served (dealt, never answered) and Again (a neutral skip, and what a lost duel side becomes)
    /// are not verdicts, and asking a player to guess a verdict that was never given would be
    /// inventing the answer. Here the friend has 2 verdicts + 6 non-verdicts; if Served/Again leaked
    /// into the pool it would be 8 and the game would deal.
    /// </summary>
    [SkippableFact]
    public async Task Pool_ExcludesServedAndAgain_SoTheyNeverBecomeARound()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);

        await GiveRitesAsync(factory, opponentId, summoned: 1, banished: 1, served: 3, again: 3);

        using HttpClient client = await SignInAsync(factory, meId);

        Availability? availability = await client.GetFromJsonAsync<Availability>(
            $"/api/games/verdict/availability/{opponentId}");

        Assert.NotNull(availability);

        // Only the summon and the banishment counted: 2 verdicts, below the minimum.
        Assert.Equal(2, availability!.VerdictsAvailable);
        Assert.False(availability.Playable);
        Assert.Equal("too-few-verdicts", availability.Reason);

        HttpResponseMessage start = await client.PostAsJsonAsync("/api/games/verdict", new { opponentId });
        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
    }

    /// <summary>
    /// Every dealt round's band must be one the friend actually resolved — and its stored truth must
    /// match the verdict they actually gave it. This is the "nothing is invented" check at the row
    /// level: a wrong join here would score players against a fiction.
    /// </summary>
    [SkippableFact]
    public async Task Deal_OnlyUsesTheOpponentsResolvedRites_AndSnapshotsTheirRealVerdict()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 2, again: 2);

        using HttpClient client = await SignInAsync(factory, meId);

        GameDto? game = await StartAsync(client, opponentId);
        Assert.NotNull(game);

        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        List<GameRound> rounds = await db.GameRounds.Where(r => r.GameId == game!.Id).ToListAsync();

        Dictionary<Guid, RiteState> truth = await db.Rites
            .Where(r => r.UserId == opponentId)
            .ToDictionaryAsync(r => r.ArtistId, r => r.State);

        Assert.NotEmpty(rounds);

        foreach (GameRound round in rounds)
        {
            Assert.True(truth.ContainsKey(round.ArtistId), "A round used a band the opponent never judged.");
            Assert.Equal(truth[round.ArtistId], round.Truth);
            Assert.Contains(round.Truth, new RiteState?[] { RiteState.Summoned, RiteState.Banished });
        }
    }

    /// <summary>An all-summoned friend cannot be guessed: every answer would be the same word.</summary>
    [SkippableFact]
    public async Task Availability_ReportsNoBanishments_WhenTheFriendHasNeverBanished()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 8, banished: 0, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        Availability? availability = await client.GetFromJsonAsync<Availability>(
            $"/api/games/verdict/availability/{opponentId}");

        Assert.False(availability!.Playable);
        Assert.Equal("no-banishments", availability.Reason);

        HttpResponseMessage start = await client.PostAsJsonAsync("/api/games/verdict", new { opponentId });
        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
    }

    // -----------------------------------------------------------------------
    // The two gates: accepted friendship, and consent
    // -----------------------------------------------------------------------

    /// <summary>A stranger's grimoire is not a playground: no friendship, no game, not even availability.</summary>
    [SkippableFact]
    public async Task Start_IsForbidden_ForAStranger()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        HttpResponseMessage availability = await client.GetAsync($"/api/games/verdict/availability/{opponentId}");
        Assert.Equal(HttpStatusCode.Forbidden, availability.StatusCode);

        HttpResponseMessage start = await client.PostAsJsonAsync("/api/games/verdict", new { opponentId });
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
    }

    /// <summary>A pending request is not a friendship — the addressee never agreed to anything.</summary>
    [SkippableFact]
    public async Task Start_IsForbidden_OnAMerelyPendingFriendship()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId, FriendshipStatus.Pending);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        HttpResponseMessage start = await client.PostAsJsonAsync("/api/games/verdict", new { opponentId });
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
    }

    /// <summary>
    /// The consent gate, and the reason it exists: this game reveals that your friend BANISHED a
    /// band — a negative judgement that no endpoint has ever shown to anybody but its author. A friend
    /// who never opted in is not playable, however good a friend they are.
    /// </summary>
    [SkippableFact]
    public async Task Start_IsForbidden_WhenTheOpponentNeverOptedIn()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: null);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        Availability? availability = await client.GetFromJsonAsync<Availability>(
            $"/api/games/verdict/availability/{opponentId}");

        Assert.False(availability!.Playable);
        Assert.Equal("opponent-has-not-opted-in", availability.Reason);

        // And the write path refuses on its own, never trusting that the client asked first.
        HttpResponseMessage start = await client.PostAsJsonAsync("/api/games/verdict", new { opponentId });
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
    }

    /// <summary>Opting out is a decision, and it refuses exactly as hard as never having been asked.</summary>
    [SkippableFact]
    public async Task Start_IsForbidden_WhenTheOpponentOptedOut()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: false);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        HttpResponseMessage start = await client.PostAsJsonAsync("/api/games/verdict", new { opponentId });
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Blind: the game cannot be won by reading the response
    // -----------------------------------------------------------------------

    /// <summary>
    /// The anti-cheat check over the wire, not just over the mapper. A dealt game's rounds must carry
    /// no band, no truth and no answer — otherwise the game is won with devtools open, and it would be
    /// won silently: a leaking response is a 200 of the right shape.
    /// </summary>
    [SkippableFact]
    public async Task Start_ServesEveryRoundBlind()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/games/verdict", new { opponentId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Read the RAW body: a typed DTO could hide a leaking field behind a property that is not
        // mapped. What the player's browser receives is what is asserted on.
        string body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Summoned", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Banished", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Verdict Band", body, StringComparison.OrdinalIgnoreCase);

        GameDto? game = await response.Content.ReadFromJsonAsync<GameDto>();
        Assert.NotNull(game);
        Assert.All(game!.Rounds, r =>
        {
            Assert.Null(r.Truth);
            Assert.Null(r.Answer);
            Assert.Null(r.Correct);
            Assert.Null(r.Artist);
            Assert.Contains("/audio", r.AudioUrl);
        });
    }

    /// <summary>Resuming a game after a reload must stay blind on the rounds still unanswered.</summary>
    [SkippableFact]
    public async Task Game_StaysBlind_OnResume_ForUnansweredRounds()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);

        GameDto? game = await StartAsync(client, opponentId);
        await AnswerAsync(client, game!.Rounds[0].Token, "summon");

        GameDto? resumed = await client.GetFromJsonAsync<GameDto>($"/api/games/verdict/{game.Id}");
        Assert.NotNull(resumed);

        // The answered round is open; every other round is still shut.
        RoundDto answered = resumed!.Rounds.Single(r => r.Token == game.Rounds[0].Token);
        Assert.NotNull(answered.Truth);
        Assert.NotNull(answered.Artist);

        Assert.All(resumed.Rounds.Where(r => r.Token != game.Rounds[0].Token), r =>
        {
            Assert.Null(r.Truth);
            Assert.Null(r.Artist);
        });
    }

    /// <summary>A game belongs to the player who was dealt it — not to the friend being guessed.</summary>
    [SkippableFact]
    public async Task Game_IsNotReadable_ByTheOpponentBeingGuessed()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GameDto? game = await StartAsync(client, opponentId);

        using HttpClient theirs = await SignInAsync(factory, opponentId);
        HttpResponseMessage response = await theirs.GetAsync($"/api/games/verdict/{game!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Answering and scoring
    // -----------------------------------------------------------------------

    /// <summary>
    /// Playing a whole game perfectly, by reading each round's truth from the database and answering
    /// it. Scores 5/5 — which proves the scoring counts real matches — and the last answer must both
    /// finish the game and drop the turn into the opponent's inbox.
    /// </summary>
    [SkippableFact]
    public async Task AnsweringEveryRoundCorrectly_ScoresFull_FinishesTheGame_AndNotifiesTheOpponent()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 6, banished: 6, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GameDto? game = await StartAsync(client, opponentId);
        Assert.NotNull(game);

        Dictionary<Guid, RiteState> truths = await RoundTruthsAsync(factory, game!.Id);

        AnswerResult? last = null;

        foreach (RoundDto round in game.Rounds)
        {
            last = await AnswerAsync(client, round.Token, truths[round.Token] == RiteState.Summoned ? "summon" : "banish");
            Assert.True(last!.Correct);
        }

        Assert.NotNull(last);
        Assert.True(last!.Finished);
        Assert.Equal(game.Rounds.Count, last.Score.Correct);
        Assert.Equal(game.Rounds.Count, last.Score.Answered);
        Assert.Equal(game.Rounds.Count, last.Score.Total);

        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        Assert.Equal(GameStatus.Finished, (await db.Games.FirstAsync(g => g.Id == game.Id)).Status);

        // The turn hand-off: the friend learns their ear was read, and how well.
        Notification notification = await db.Notifications
            .SingleAsync(n => n.UserId == opponentId && n.Type == NotificationType.VerdictGamePlayed);

        Assert.Equal(meId, notification.ActorId);
        Assert.Contains(game.Rounds.Count.ToString(), notification.PayloadJson);
    }

    /// <summary>Answering every round wrong scores zero — the mirror of the perfect game.</summary>
    [SkippableFact]
    public async Task AnsweringEveryRoundWrongly_ScoresZero()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 6, banished: 6, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GameDto? game = await StartAsync(client, opponentId);

        Dictionary<Guid, RiteState> truths = await RoundTruthsAsync(factory, game!.Id);

        AnswerResult? last = null;

        foreach (RoundDto round in game.Rounds)
        {
            // Deliberately the opposite of the truth every time.
            last = await AnswerAsync(client, round.Token, truths[round.Token] == RiteState.Summoned ? "banish" : "summon");
            Assert.False(last!.Correct);
        }

        Assert.Equal(0, last!.Score.Correct);
        Assert.Equal(game.Rounds.Count, last.Score.Answered);
    }

    /// <summary>
    /// A round is answered once. Without this the reveal makes the retry free: answer, see the truth,
    /// answer again correctly. The 409 is what keeps the score meaning anything.
    /// </summary>
    [SkippableFact]
    public async Task AnsweringARoundTwice_IsRejected()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GameDto? game = await StartAsync(client, opponentId);

        await AnswerAsync(client, game!.Rounds[0].Token, "summon");

        HttpResponseMessage again = await client.PostAsJsonAsync(
            $"/api/games/rounds/{game.Rounds[0].Token}/answer", new { verdict = "banish" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    /// <summary>Somebody else's round is not answerable — and a 404 does not even confirm it exists.</summary>
    [SkippableFact]
    public async Task AnsweringSomeoneElsesRound_IsRejected()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        Guid strangerId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GameDto? game = await StartAsync(client, opponentId);

        using HttpClient stranger = await SignInAsync(factory, strangerId);
        HttpResponseMessage response = await stranger.PostAsJsonAsync(
            $"/api/games/rounds/{game!.Rounds[0].Token}/answer", new { verdict = "summon" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>"Again" is a skip, not a verdict: it is not in the pool, so it is not an answer either.</summary>
    [SkippableFact]
    public async Task AnsweringWithANonVerdict_IsRejected()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient client = await SignInAsync(factory, meId);
        GameDto? game = await StartAsync(client, opponentId);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/games/rounds/{game!.Rounds[0].Token}/answer", new { verdict = "again" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Consent round trip
    // -----------------------------------------------------------------------

    /// <summary>
    /// Consent starts NULL, not false: "never asked" and "asked and declined" are different facts,
    /// and the front shows the first as a question rather than a setting already answered.
    /// </summary>
    [SkippableFact]
    public async Task Consent_StartsUnansweredAndRoundTrips()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid meId = await CreateUserAsync(factory, optIn: null);
        using HttpClient client = await SignInAsync(factory, meId);

        Consent? initial = await client.GetFromJsonAsync<Consent>("/api/games/verdict/consent");
        Assert.Null(initial!.OptIn);

        HttpResponseMessage set = await client.PutAsJsonAsync("/api/games/verdict/consent", new { optIn = true });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        Consent? after = await client.GetFromJsonAsync<Consent>("/api/games/verdict/consent");
        Assert.True(after!.OptIn);

        await client.PutAsJsonAsync("/api/games/verdict/consent", new { optIn = false });

        Consent? revoked = await client.GetFromJsonAsync<Consent>("/api/games/verdict/consent");
        Assert.False(revoked!.OptIn);
    }

    /// <summary>The history carries both sides of the turn: the games I played and the ones played on me.</summary>
    [SkippableFact]
    public async Task Games_ListsBothSidesOfTheTurn()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid opponentId = await CreateUserAsync(factory, optIn: true);
        Guid meId = await CreateUserAsync(factory, optIn: true);
        await BefriendAsync(factory, meId, opponentId);
        await GiveRitesAsync(factory, opponentId, summoned: 4, banished: 3, served: 0, again: 0);
        await GiveRitesAsync(factory, meId, summoned: 4, banished: 3, served: 0, again: 0);

        using HttpClient mine = await SignInAsync(factory, meId);
        await StartAsync(mine, opponentId);

        using HttpClient theirs = await SignInAsync(factory, opponentId);
        await StartAsync(theirs, meId);

        List<GameSummary>? list = await mine.GetFromJsonAsync<List<GameSummary>>("/api/games/verdict");

        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        Assert.Contains(list, g => g.PlayedByMe && g.OtherUserId == opponentId);
        Assert.Contains(list, g => !g.PlayedByMe && g.OtherUserId == opponentId);
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

    /// <summary>Registers a user through the real auth endpoint, then sets their consent directly.</summary>
    private static async Task<Guid> CreateUserAsync(WebApplicationFactory<Program> factory, bool? optIn)
    {
        using HttpClient client = factory.CreateClient();

        string email = $"game-{Guid.NewGuid():N}@example.com";
        HttpResponseMessage register = await client.PostAsJsonAsync(
            "/api/auth/register", new { email, password = "Passw0rd!23" });

        register.EnsureSuccessStatusCode();

        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        GrimoireUser user = await db.Users.FirstAsync(u => u.Email == email);
        user.Handle = $"h{Guid.NewGuid():N}"[..12];
        user.VerdictGameOptIn = optIn;
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
    /// just-in-time probe short-circuits on the cache — these tests make no network call.
    /// </summary>
    private static async Task GiveRitesAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        int summoned,
        int banished,
        int served,
        int again)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await AddRitesAsync(db, userId, RiteState.Summoned, summoned);
        await AddRitesAsync(db, userId, RiteState.Banished, banished);
        await AddRitesAsync(db, userId, RiteState.Served, served);
        await AddRitesAsync(db, userId, RiteState.Again, again);

        await db.SaveChangesAsync();
    }

    private static async Task AddRitesAsync(GrimoireDbContext db, Guid userId, RiteState state, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float[] vector = new float[768];
            Array.Fill(vector, 0.1f);

            Artist artist = new()
            {
                Id = Guid.NewGuid(),
                Mbid = Guid.NewGuid(),
                Name = $"Verdict Band {state} {i} {Guid.NewGuid():N}"[..28],
                SortName = "Verdict Band",
                Kind = ArtistKind.Group,
                Country = "XX",
                FormedYear = 1990,
                Tags = ["verdict-test"],
                Embedding = new Vector(vector),
                PreviewUrl = AllowlistedPreview,
            };

            db.Artists.Add(artist);
            db.Rites.Add(new Rite
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ArtistId = artist.Id,
                State = state,
                ServedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ResolvedAt = state == RiteState.Served ? null : DateTimeOffset.UtcNow,
            });
        }

        await Task.CompletedTask;
    }

    private static async Task<GameDto?> StartAsync(HttpClient client, Guid opponentId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/games/verdict", new { opponentId });
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<GameDto>();
    }

    private static async Task<AnswerResult?> AnswerAsync(HttpClient client, Guid token, string verdict)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/games/rounds/{token}/answer", new { verdict });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AnswerResult>();
    }

    /// <summary>Reads each round's stored truth straight from the database — the tests' oracle.</summary>
    private static async Task<Dictionary<Guid, RiteState>> RoundTruthsAsync(
        WebApplicationFactory<Program> factory,
        Guid gameId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        return await db.GameRounds
            .Where(r => r.GameId == gameId && r.Truth != null)
            .ToDictionaryAsync(r => r.Id, r => r.Truth!.Value);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record Availability(bool Playable, string? Reason, int VerdictsAvailable);

    private sealed record RoundDto(
        Guid Token,
        int Ordinal,
        string AudioUrl,
        ArtistSummary? Artist,
        string? Truth,
        string? Answer,
        bool? Correct);

    private sealed record ArtistSummary(Guid Id, string Name);

    private sealed record GameDto(
        Guid Id,
        Guid OpponentId,
        string? OpponentHandle,
        string Status,
        IReadOnlyList<RoundDto> Rounds,
        Score Score);

    private sealed record Score(int Correct, int Answered, int Total);

    private sealed record AnswerResult(bool Correct, string Truth, Score Score, bool Finished);

    private sealed record GameSummary(Guid Id, bool PlayedByMe, Guid OtherUserId, string? OtherHandle, string Status);

    private sealed record Consent(bool? OptIn);

    private sealed record AuthTokens(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt);
}
