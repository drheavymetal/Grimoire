using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
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
/// End-to-end checks of the just-in-time preview serve (DECISIONS D25/D19) against a real PostgreSQL:
/// the ring now draws from the embedded catalogue (no <c>preview_url</c> pre-filter), and the serve
/// path resolves audibility per candidate, skipping the inaudible ones. Uses a throwaway
/// "grimoire_test_jit" database it creates and drops, never the development database. These bite
/// without a network call — audible bands are pre-cached and inaudible ones are pre-marked probed, so
/// the resolver's negative cache short-circuits every lookup. Skipped cleanly when PostgreSQL is down.
/// </summary>
public class RiteServeJitTests : IAsyncLifetime
{
    private const string MaintenanceConnectionString =
        "Host=localhost;Port=5433;Database=grimoire;Username=grimoire;Password=grimoire;Timeout=3;Command Timeout=5";

    private const string AllowlistedPreview = "https://audio-ssl.itunes.apple.com/preview/fixture.m4a";

    // A database per test instance: xUnit may run the class's tests concurrently, and a shared name
    // would let one test's DROP ... WITH FORCE terminate another's connections mid-migration.
    private readonly string _databaseName = $"grimoire_test_jit_{Guid.NewGuid():N}";

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

    /// <summary>
    /// The ring no longer requires a preview: a band with an embedding but a null <c>preview_url</c>
    /// must still be reachable, because the preview is resolved at serve time. Under the old filter
    /// (<c>preview_url IS NOT NULL</c>) the ring would be empty and this returns nothing.
    /// </summary>
    [SkippableFact]
    public async Task Ring_IncludesEmbeddedBands_WithoutAPreviewUrl()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();

        Guid bandId;
        float[] embedding = Embedding();

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
            bandId = (await Insert(db, "JIT Ring Band", embedding, previewUrl: null, probed: false)).Id;

            RiteEngine engine = scope.ServiceProvider.GetRequiredService<RiteEngine>();

            IReadOnlyList<RiteCandidate> ring = await engine.FindManyAsync(
                Guid.NewGuid(), new Vector(embedding), null, 0.5, new RiteFilters(null, null, null), 12, default);

            Assert.Contains(ring, c => c.ArtistId == bandId);
        }
    }

    /// <summary>
    /// A serve where every reachable band has been probed and found inaudible must return 204 (a
    /// designed empty state), not a silent 200. This is the audibility skip: if the serve stopped
    /// filtering on audibility it would serve one of these silent bands. No network — all are
    /// pre-marked probed, so the negative cache short-circuits without a lookup.
    /// </summary>
    [SkippableFact]
    public async Task Serve_ReturnsNoContent_WhenEveryReachableBandIsInaudible()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();
        float[] embedding = Embedding();

        Guid seedId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

            seedId = (await Insert(db, "JIT Silent Seed", embedding, previewUrl: null, probed: true)).Id;

            for (int i = 0; i < 5; i++)
            {
                await Insert(db, $"JIT Silent {i}", embedding, previewUrl: null, probed: true);
            }
        }

        using HttpClient client = await AuthenticatedClient(factory);
        await Seed(client, seedId);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/rite/serve", new { comfort = 0.5 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// A serve with one already-audible band (a cached, allow-listed preview) among several inaudible
    /// probed ones must serve the audible band, cached preview and all — proving the iteration skips
    /// the inaudible candidates and lands on the one that can sound.
    /// </summary>
    [SkippableFact]
    public async Task Serve_PicksTheAudibleBand_SkippingProbedInaudibleOnes()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = Factory();
        float[] embedding = Embedding();

        Guid audibleId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

            audibleId = (await Insert(db, "JIT Audible", embedding, previewUrl: AllowlistedPreview, probed: false)).Id;

            for (int i = 0; i < 4; i++)
            {
                await Insert(db, $"JIT Muted {i}", embedding, previewUrl: null, probed: true);
            }
        }

        using HttpClient client = await AuthenticatedClient(factory);
        await Seed(client, audibleId);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/rite/serve", new { comfort = 0.5 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ServedRite? served = await response.Content.ReadFromJsonAsync<ServedRite>();
        Assert.NotNull(served);

        using IServiceScope check = factory.Services.CreateScope();
        GrimoireDbContext verify = check.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        Guid servedArtistId = await verify.Rites
            .Where(r => r.Id == served!.Token)
            .Select(r => r.ArtistId)
            .FirstAsync();

        Assert.Equal(audibleId, servedArtistId);
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

    private static async Task<Artist> Insert(GrimoireDbContext db, string name, float[] embedding, string? previewUrl, bool probed)
    {
        Artist artist = new()
        {
            Id = Guid.NewGuid(),
            Mbid = Guid.NewGuid(),
            Name = name,
            SortName = name,
            Kind = ArtistKind.Group,
            Country = "XX",
            FormedYear = 1990,
            Tags = ["jit-test"],
            Embedding = new Vector(embedding),
            PreviewUrl = previewUrl,
            Links = probed ? new Dictionary<string, string> { ["listen:spotify"] = "https://open.spotify.com/search/x" } : null,
        };

        db.Artists.Add(artist);
        await db.SaveChangesAsync();

        return artist;
    }

    private static async Task<HttpClient> AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();

        string email = $"jit-{Guid.NewGuid():N}@example.com";
        HttpResponseMessage register = await client.PostAsJsonAsync(
            "/api/auth/register", new { email, password = "Passw0rd!23" });

        register.EnsureSuccessStatusCode();

        AuthTokens? tokens = await register.Content.ReadFromJsonAsync<AuthTokens>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return client;
    }

    private static async Task Seed(HttpClient client, Guid artistId)
    {
        HttpResponseMessage seed = await client.PostAsJsonAsync(
            "/api/rite/seed", new { artistIds = new[] { artistId } });

        seed.EnsureSuccessStatusCode();
    }

    private static float[] Embedding()
    {
        // Identical vectors across fixtures: every distance to a taste equal to this vector is 0, so
        // the percentile ring collapses to include them all — the fixtures are the whole ring.
        float[] vector = new float[768];
        Array.Fill(vector, 0.1f);

        return vector;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record ServedRite(Guid Token, double RiskPercentile, string AudioUrl);

    private sealed record AuthTokens(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt);
}
