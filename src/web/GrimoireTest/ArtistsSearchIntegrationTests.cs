using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// Exercises GET /api/artists?q= end to end through WebApplicationFactory against a real
/// PostgreSQL instance, so the trigram search runs as real SQL. It must NEVER touch the
/// development database ("grimoire") that the app serves: it creates a throwaway database
/// "grimoire_test", points the app at it, and drops it afterwards (WITH FORCE, so lingering
/// pooled connections cannot block the drop). The fixture artist uses a synthetic,
/// collision-proof name and a random MBID, and is deleted by that MBID before the database
/// is dropped. If PostgreSQL is not reachable — as when this test runs before
/// `docker compose up` in the verification gate — it is skipped cleanly, not faked.
/// </summary>
public class ArtistsSearchIntegrationTests : IAsyncLifetime
{
    private const string MaintenanceConnectionString =
        "Host=localhost;Port=5433;Database=grimoire;Username=grimoire;Password=grimoire;Timeout=3;Command Timeout=5";

    private const string TestDatabaseName = "grimoire_test";

    private const string TestConnectionString =
        "Host=localhost;Port=5433;Database=grimoire_test;Username=grimoire;Password=grimoire";

    private readonly Guid _fixtureMbid = Guid.NewGuid();
    private readonly string _fixtureName = $"ZZ Test Artist {Guid.NewGuid():N}";

    private bool _databaseReady;
    private string _skipReason = "PostgreSQL is not reachable on localhost:5433 (start build/dev/docker-compose.yml).";

    public async Task InitializeAsync()
    {
        try
        {
            await using NpgsqlConnection maintenance = new(MaintenanceConnectionString);
            await maintenance.OpenAsync();

            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS {TestDatabaseName} WITH (FORCE);");
            await ExecuteAsync(maintenance, $"CREATE DATABASE {TestDatabaseName};");

            _databaseReady = true;
        }
        catch (NpgsqlException ex)
        {
            _skipReason = $"Could not provision the '{TestDatabaseName}' database: {ex.Message}";
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

        // Release any pooled connections to the test database before dropping it.
        NpgsqlConnection.ClearAllPools();

        await using NpgsqlConnection maintenance = new(MaintenanceConnectionString);
        await maintenance.OpenAsync();
        await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS {TestDatabaseName} WITH (FORCE);");
    }

    [SkippableFact]
    public async Task Search_ReturnsTrigramMatch_FromRealDatabase()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                // Redirect the app's DbContext to the throwaway test database. Scoped to this
                // factory only — the development database is never opened.
                ServiceDescriptor? optionsDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<GrimoireDbContext>));

                if (optionsDescriptor is not null)
                {
                    services.Remove(optionsDescriptor);
                }

                services.AddDbContext<GrimoireDbContext>(options =>
                    options.UseNpgsql(TestConnectionString, npgsql => npgsql.UseVector())
                        .UseSnakeCaseNamingConvention());
            }));

        try
        {
            // Insert the synthetic fixture (start-up migration has already created the schema).
            using (IServiceScope scope = factory.Services.CreateScope())
            {
                GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
                db.Artists.Add(new Artist
                {
                    Id = Guid.NewGuid(),
                    Mbid = _fixtureMbid,
                    Name = _fixtureName,
                    SortName = _fixtureName,
                    Kind = ArtistKind.Group,
                    Country = "XX",
                    FormedYear = 1999,
                    Tags = ["integration-test"],
                });
                await db.SaveChangesAsync();
            }

            using HttpClient client = factory.CreateClient();

            // Drop the last character of the name: a deliberate typo the trigram search must
            // still match. Proves fuzzy matching works on a name that cannot collide.
            string query = _fixtureName[..^1];
            HttpResponseMessage response = await client.GetAsync(
                $"/api/artists?q={Uri.EscapeDataString(query)}&limit=5");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            List<ArtistResult>? results = await response.Content.ReadFromJsonAsync<List<ArtistResult>>();

            Assert.NotNull(results);
            Assert.Contains(results!, r => r.Name == _fixtureName);
        }
        finally
        {
            // Clean up the fixture by its specific MBID, even on failure. (The whole test
            // database is dropped in DisposeAsync regardless; this is belt-and-suspenders.)
            using IServiceScope scope = factory.Services.CreateScope();
            GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
            await db.Artists.Where(a => a.Mbid == _fixtureMbid).ExecuteDeleteAsync();
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private record ArtistResult(Guid Id, string Name, string? Country, int? FormedYear, string? Rank);
}
