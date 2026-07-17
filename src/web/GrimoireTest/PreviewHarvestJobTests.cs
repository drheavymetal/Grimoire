using Grimoire.Library.Data;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Worker.Preview;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// The preview pass against a real PostgreSQL: what it writes, what it refuses to write, and what it
/// must never touch. A throwaway "grimoire_test_previews_*" database it creates and drops — never the
/// development one — and stub sources, so no request leaves the machine. Skipped cleanly when
/// PostgreSQL is down.
///
/// <para>
/// These exist because the pure rules in <see cref="ArtistPreviewsTests"/> cannot see the two things
/// that would actually hurt. First, the D61 marker rule: stamping <c>previews_checked_at</c> after a
/// source failed to answer records an outage as "this band has no other clips", for ever and
/// indistinguishably — a lie no later run would ever revisit, and the exact shape of the bug that hit
/// all three crawls at once (MEMORY §6f). Second, the primary key: a re-run that re-adds a stored clip
/// is not a duplicate row, it is a crashed pass, and only a database can prove it does not happen.
/// </para>
/// </summary>
public class PreviewHarvestJobTests : IAsyncLifetime
{
    private const string MaintenanceConnectionString =
        "Host=localhost;Port=5433;Database=grimoire;Username=grimoire;Password=grimoire;Timeout=3;Command Timeout=5";

    private const string RiteCut = "https://audio-ssl.itunes.apple.com/preview/rite-cut.m4a";

    private readonly string _databaseName = $"grimoire_test_previews_{Guid.NewGuid():N}";

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
            _skipReason = $"PostgreSQL is not reachable on localhost:5433: {ex.Message}";
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            _skipReason = $"PostgreSQL is not reachable on localhost:5433: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (!_databaseReady)
        {
            return;
        }

        await using NpgsqlConnection maintenance = new(MaintenanceConnectionString);
        await maintenance.OpenAsync();
        await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE);");
    }

    [SkippableFact]
    public async Task Resolve_KeepsEveryClip_AndStampsTheHarvest()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        Guid id = await SeedAsync(band => band.Tags = ["black metal"]);

        await RunAsync(ITunes(Answer(
            ("https://audio-ssl.itunes.apple.com/1.m4a", "Transilvanian Hunger"),
            ("https://audio-ssl.itunes.apple.com/2.m4a", "Slottet i det fjerne"))));

        await using GrimoireDbContext db = Context();
        Artist band = await db.Artists.Include(a => a.Previews).SingleAsync(a => a.Id == id);

        // The Rite's cut is the first match, exactly as it always was.
        Assert.Equal("https://audio-ssl.itunes.apple.com/1.m4a", band.PreviewUrl);

        // And the clip we used to drop on the floor is now a row (D67).
        Assert.Equal(2, band.Previews.Count);
        Assert.Contains(band.Previews, p => p.TrackTitle == "Slottet i det fjerne");
        Assert.NotNull(band.PreviewsCheckedAt);
    }

    [SkippableFact]
    public async Task Harvest_CollectsAlternatesForABandTheRiteAlreadyResolved_WithoutTouchingItsCut()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        // The state nearly every audible band in production is actually in: audio resolved just-in-time
        // at serve time (D40), so it is already marked probed and phase one can never see it again.
        Guid id = await SeedAsync(band =>
        {
            band.PreviewUrl = RiteCut;
            band.Links = Probed();
        });

        await RunAsync(ITunes(Answer(
            ("https://audio-ssl.itunes.apple.com/other.m4a", "Funeral Fog"),
            (RiteCut, "Freezing Moon"))));

        await using GrimoireDbContext db = Context();
        Artist band = await db.Artists.Include(a => a.Previews).SingleAsync(a => a.Id == id);

        // THE invariant of this whole change. The Rite's pool is `preview_url IS NOT NULL`; a harvest
        // that rewrote this column could only ever silence bands.
        Assert.Equal(RiteCut, band.PreviewUrl);

        Assert.Equal(2, band.Previews.Count);
        Assert.NotNull(band.PreviewsCheckedAt);
    }

    [SkippableFact]
    public async Task Harvest_SkipsInaudibleBands_AndAsksNobodyAboutThem()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        // Probed and found silent: there is nothing to harvest, and re-asking every run to rediscover
        // that is precisely the loop D61 exists to stop.
        Guid id = await SeedAsync(band =>
        {
            band.PreviewUrl = null;
            band.Links = Probed();
        });

        StubSource itunes = ITunes(Answer(("https://audio-ssl.itunes.apple.com/1.m4a", "Anything")));
        await RunAsync(itunes);

        await using GrimoireDbContext db = Context();
        Artist band = await db.Artists.Include(a => a.Previews).SingleAsync(a => a.Id == id);

        Assert.Empty(band.Previews);
        Assert.Null(band.PreviewsCheckedAt);
        Assert.Equal(0, itunes.Calls);
    }

    [SkippableFact]
    public async Task Harvest_WhenASourceWillNotAnswer_DoesNotStamp_AndARerunResolvesIt()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        // The D61 rule, end to end. A 429 says nothing about the band; sealing it here would record the
        // outage as "no other clips" for ever, and nothing revisits a stamped row.
        //
        // TWO bands, and that is the whole point of the fixture. With one, this test passes even if the
        // marker is stamped unconditionally — the pass skips the save, so the lie never reaches the
        // database and the assertion cannot see it. But the stamp lands on a TRACKED entity, so the very
        // next band's SaveChangesAsync flushes it too: the poisoning would appear only when a run had a
        // failure and a success in it, which is every real run and no single-row test.
        Guid failed = await SeedAsync(band =>
        {
            band.Name = "Unreachable";
            band.PreviewUrl = RiteCut;
            band.Links = Probed("Unreachable");
        });

        Guid fine = await SeedAsync(band =>
        {
            band.Name = "Zealous";
            band.PreviewUrl = "https://audio-ssl.itunes.apple.com/preview/zealous.m4a";
            band.Links = Probed("Zealous");
        });

        await RunAsync(ITunes(band => band.Name == "Unreachable"
            ? EnrichmentResult.Unavailable
            : Answer(("https://audio-ssl.itunes.apple.com/zealous-2.m4a", "Second Cut"))));

        await using (GrimoireDbContext db = Context())
        {
            Artist band = await db.Artists.Include(a => a.Previews).SingleAsync(a => a.Id == failed);

            Assert.Null(band.PreviewsCheckedAt);
            Assert.Empty(band.Previews);

            // Still audible: an outage must not cost the band the cut it already had.
            Assert.Equal(RiteCut, band.PreviewUrl);

            // The band that answered was harvested and sealed in the same run, which is what makes the
            // assertion above about the RULE rather than about the pass having done nothing at all.
            Artist ok = await db.Artists.Include(a => a.Previews).SingleAsync(a => a.Id == fine);
            Assert.NotNull(ok.PreviewsCheckedAt);
            Assert.Single(ok.Previews);
        }

        // The failed band is still pending, so the next run picks it up — the "loud" half of D61's bargain.
        await RunAsync(ITunes(Answer(("https://audio-ssl.itunes.apple.com/late.m4a", "Funeral Fog"))));

        await using (GrimoireDbContext db = Context())
        {
            Artist band = await db.Artists.Include(a => a.Previews).SingleAsync(a => a.Id == failed);

            Assert.NotNull(band.PreviewsCheckedAt);
            Assert.Single(band.Previews);
        }
    }

    [SkippableFact]
    public async Task Harvest_IsIdempotent_ARerunNeitherDuplicatesNorCrashes()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        Guid id = await SeedAsync(band =>
        {
            band.PreviewUrl = RiteCut;
            band.Links = Probed();
        });

        (string, string?)[] answer =
        [
            ("https://audio-ssl.itunes.apple.com/1.m4a", "Freezing Moon"),
            ("https://audio-ssl.itunes.apple.com/2.m4a", null),
        ];

        await RunAsync(ITunes(Answer(answer)));

        // Clear the marker so the second run genuinely re-harvests the same band with the same answers —
        // which is exactly what a re-run after a transient failure does. Rows are keyed on (artist, url),
        // so a pass that re-adds them does not duplicate: it throws.
        await using (GrimoireDbContext reset = Context())
        {
            Artist band = await reset.Artists.SingleAsync(a => a.Id == id);
            band.PreviewsCheckedAt = null;
            await reset.SaveChangesAsync();
        }

        await RunAsync(ITunes(Answer(answer)), expectNoErrors: true);

        await using GrimoireDbContext db = Context();
        Artist harvested = await db.Artists.Include(a => a.Previews).SingleAsync(a => a.Id == id);

        Assert.Equal(2, harvested.Previews.Count);
        Assert.NotNull(harvested.PreviewsCheckedAt);
    }

    [SkippableFact]
    public async Task EverySourceDisabled_RecordsNothing_RatherThanSealingSilence()
    {
        Skip.IfNot(_databaseReady, _skipReason);

        // Poisoning by configuration rather than by 429. A disabled source is skipped and counted as a
        // definitive "nothing here" (D9), so with every source off, "everyone answered" is vacuously
        // true and the pass would stamp bands it never asked about — permanently, and without one
        // request leaving the machine. Found while measuring the pass, not by a failing test.
        Guid id = await SeedAsync(band =>
        {
            band.PreviewUrl = RiteCut;
            band.Links = Probed();
        });

        await RunAsync(sources: []);

        await using GrimoireDbContext db = Context();
        Artist band = await db.Artists.Include(a => a.Previews).SingleAsync(a => a.Id == id);

        Assert.Null(band.PreviewsCheckedAt);
        Assert.Empty(band.Previews);
        Assert.Equal(RiteCut, band.PreviewUrl);
    }

    // --- Fixtures and plumbing ---

    private static Dictionary<string, string> Probed(string name = "Darkthrone")
    {
        return StreamingLinks.Build(name, null, null);
    }

    private static EnrichmentResult Answer(params (string Url, string? Title)[] tracks)
    {
        return EnrichmentResult.Matched(new ArtistEnrichment
        {
            PreviewUrl = tracks.Length == 0 ? null : tracks[0].Url,
            Previews = tracks.Select(t => new PreviewCandidate(t.Url, "iTunes", t.Title)).ToList(),
        });
    }

    private static StubSource ITunes(EnrichmentResult result)
    {
        return new StubSource("iTunes", _ => result);
    }

    /// <summary>A source whose answer depends on the band — the only way to put a failure and a success in one run.</summary>
    private static StubSource ITunes(Func<Artist, EnrichmentResult> respond)
    {
        return new StubSource("iTunes", respond);
    }

    private async Task<Guid> SeedAsync(Action<Artist> configure)
    {
        await using GrimoireDbContext db = Context();
        await db.Database.MigrateAsync();

        Artist band = new()
        {
            Id = Guid.NewGuid(),
            Mbid = Guid.NewGuid(),
            Name = "Darkthrone",
            Kind = ArtistKind.Group,
        };

        configure(band);

        db.Artists.Add(band);
        await db.SaveChangesAsync();

        return band.Id;
    }

    /// <summary>
    /// Runs the pass once, as the host would. <paramref name="expectNoErrors"/> matters more than it
    /// looks: <c>WorkerJob</c> logs and swallows every exception, so a pass that crashed on a primary
    /// key leaves a database that merely looks unchanged. Without reading the log back, an idempotency
    /// test would happily pass over a job that blew up.
    /// </summary>
    private Task RunAsync(StubSource itunes, bool expectNoErrors = false)
    {
        return RunAsync([itunes], expectNoErrors);
    }

    private async Task RunAsync(IEnrichmentSource[] sources, bool expectNoErrors = false)
    {
        ServiceCollection services = new();
        services.AddDbContext<GrimoireDbContext>(options =>
            options.UseNpgsql(TestConnectionString, npgsql => npgsql.UseVector()).UseSnakeCaseNamingConvention());

        await using ServiceProvider provider = services.BuildServiceProvider();

        RecordingLogger logger = new();
        StubLifetime lifetime = new();

        PreviewJob job = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            sources,
            new PreviewOptions { Limit = 60 },
            lifetime,
            logger);

        await job.StartAsync(CancellationToken.None);
        await job.StopAsync(CancellationToken.None);

        if (expectNoErrors)
        {
            Assert.Empty(logger.Errors);
        }
    }

    private GrimoireDbContext Context()
    {
        DbContextOptions<GrimoireDbContext> options = new DbContextOptionsBuilder<GrimoireDbContext>()
            .UseNpgsql(TestConnectionString, npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GrimoireDbContext(options);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A source that answers from memory and counts how often it was asked.</summary>
    private sealed class StubSource : IEnrichmentSource
    {
        private readonly Func<Artist, EnrichmentResult> _respond;

        public StubSource(string name, Func<Artist, EnrichmentResult> respond)
        {
            Name = name;
            _respond = respond;
        }

        public string Name { get; }

        public bool Enabled => true;

        public int Calls { get; private set; }

        public Task<EnrichmentResult> FetchAsync(Artist artist, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_respond(artist));
        }
    }

    private sealed class RecordingLogger : ILogger<PreviewJob>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add($"{formatter(state, exception)} :: {exception}");
            }
        }
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
