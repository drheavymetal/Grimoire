using System.Net;
using Grimoire.Library.Data;
using Grimoire.Library.Enrichment;
using Grimoire.Worker;
using Grimoire.Worker.Embedding;
using Grimoire.Worker.Listeners;
using Grimoire.Worker.MusicBrainz;
using Grimoire.Worker.Preview;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Serilog;
using Serilog.Events;

string[] knownVerbs = ["seed", "edges", "previews", "listeners", "embeddings", "stats"];
string? verb = args
    .Select(a => a.ToLowerInvariant())
    .FirstOrDefault(a => knownVerbs.Contains(a));

if (verb is null)
{
    Console.WriteLine("Grimoire worker. Usage:");
    Console.WriteLine("  dotnet run --project src/console/server -- <verb>");
    Console.WriteLine();
    Console.WriteLine("Verbs (movement II ETL, run in this order the first time):");
    Console.WriteLine("  seed        Fetch artists + release-groups from MusicBrainz (movement I).");
    Console.WriteLine("  edges       Import member_of relations (dates + instruments) from MusicBrainz.");
    Console.WriteLine("  previews    Resolve audio previews (iTunes first, Deezer complement) + streaming links.");
    Console.WriteLine("  listeners   Populate Last.fm listeners and derive rank (needs LastFm:ApiKey).");
    Console.WriteLine("  embeddings  Build centred nomic-embed-text embeddings and persist the corpus mean.");
    Console.WriteLine("  stats       Report neighbour-distance percentiles p10/p50/p90 (D26 sanity check).");
    return;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(configuration => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .WriteTo.Console());

string connectionString =
    builder.Configuration.GetConnectionString("Grimoire")
    ?? Environment.GetEnvironmentVariable("GRIMOIRE_CONNECTION")
    ?? "Host=localhost;Port=5433;Database=grimoire;Username=grimoire;Password=grimoire";

builder.Services.AddDbContext<GrimoireDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()).UseSnakeCaseNamingConvention());

switch (verb)
{
    case "seed":
        ConfigureSeed(builder);
        break;
    case "edges":
        ConfigureMusicBrainz(builder);
        builder.Services.AddHostedService<EdgesJob>();
        break;
    case "previews":
        ConfigurePreviews(builder);
        break;
    case "listeners":
        ConfigureListeners(builder);
        break;
    case "embeddings":
        ConfigureEmbeddings(builder);
        builder.Services.AddHostedService<EmbeddingJob>();
        break;
    case "stats":
        builder.Services.AddHostedService<StatsJob>();
        break;
}

IHost host = builder.Build();
await host.RunAsync();

// ---------------------------------------------------------------------------
// Per-verb wiring
// ---------------------------------------------------------------------------

static void ConfigureSeed(HostApplicationBuilder builder)
{
    int target = builder.Configuration.GetValue("Seed:Target", 300);

    if (int.TryParse(Environment.GetEnvironmentVariable("GRIMOIRE_SEED_TARGET"), out int envTarget) && envTarget > 0)
    {
        target = envTarget;
    }

    builder.Services.AddSingleton(new SeedOptions { RunSeed = true, Target = target });
    ConfigureMusicBrainz(builder);
    builder.Services.AddHostedService<MusicBrainzSeedJob>();
}

// The MusicBrainz client (1 req/s, resilient) is shared by the seed and edges verbs.
static void ConfigureMusicBrainz(HostApplicationBuilder builder)
{
    builder.Services.AddSingleton<MusicBrainzRateLimiter>();

    builder.Services.AddHttpClient<MusicBrainzClient>(client =>
        {
            client.BaseAddress = new Uri("https://musicbrainz.org/ws/2/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Grimoire/0.1 ( pmanso@go2chain.es )");
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddResilienceHandler("musicbrainz", pipeline =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 4,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                ShouldHandle = static args => ValueTask.FromResult(
                    args.Outcome.Exception is HttpRequestException
                    || args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests }
                    || args.Outcome.Result is { StatusCode: HttpStatusCode.ServiceUnavailable }),
            });
        });
}

static void ConfigurePreviews(HostApplicationBuilder builder)
{
    int limit = builder.Configuration.GetValue("Preview:Limit", 60);

    if (int.TryParse(Environment.GetEnvironmentVariable("GRIMOIRE_PREVIEW_LIMIT"), out int envLimit) && envLimit > 0)
    {
        limit = envLimit;
    }

    builder.Services.AddSingleton(new PreviewOptions { Limit = limit });

    bool itunesEnabled = builder.Configuration.GetValue("Sources:ITunes:Enabled", true);
    bool deezerEnabled = builder.Configuration.GetValue("Sources:Deezer:Enabled", true);

    AddPoliteHttpClient(builder, "itunes", "https://itunes.apple.com/");
    AddPoliteHttpClient(builder, "deezer", "https://api.deezer.com/");

    builder.Services.AddSingleton<IEnrichmentSource>(sp => new ITunesEnrichmentSource(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("itunes"),
        itunesEnabled,
        sp.GetRequiredService<ILogger<ITunesEnrichmentSource>>()));

    builder.Services.AddSingleton<IEnrichmentSource>(sp => new DeezerEnrichmentSource(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("deezer"),
        deezerEnabled,
        sp.GetRequiredService<ILogger<DeezerEnrichmentSource>>()));

    builder.Services.AddHostedService<PreviewJob>();
}

static void ConfigureListeners(HostApplicationBuilder builder)
{
    int limit = builder.Configuration.GetValue("Listeners:Limit", 500);

    if (int.TryParse(Environment.GetEnvironmentVariable("GRIMOIRE_LISTENERS_LIMIT"), out int envLimit) && envLimit > 0)
    {
        limit = envLimit;
    }

    builder.Services.AddSingleton(new ListenersOptions { Limit = limit });

    // Key from user-secrets (LastFm:ApiKey) with an env fallback, matching the codebase's other
    // secrets. Never logged, never committed. No key means the source is disabled and the job
    // does nothing (Invariant 5 / D9 / blocker Q5).
    string? apiKey = builder.Configuration["LastFm:ApiKey"]
        ?? Environment.GetEnvironmentVariable("GRIMOIRE_LASTFM_APIKEY");

    AddPoliteHttpClient(builder, "lastfm", "https://ws.audioscrobbler.com/");

    builder.Services.AddSingleton<IEnrichmentSource>(sp => new LastFmEnrichmentSource(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("lastfm"),
        apiKey,
        sp.GetRequiredService<ILogger<LastFmEnrichmentSource>>()));

    builder.Services.AddHostedService<ListenersJob>();
}

static void ConfigureEmbeddings(HostApplicationBuilder builder)
{
    bool enabled = builder.Configuration.GetValue("Sources:Ollama:Enabled", true);
    string baseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434/";
    string model = builder.Configuration["Ollama:Model"] ?? "nomic-embed-text";

    builder.Services.AddSingleton(new EmbeddingOptions { Enabled = enabled });

    builder.Services.AddHttpClient("ollama", client =>
    {
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(120);
    });

    builder.Services.AddSingleton(sp => new OllamaClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama"),
        model,
        sp.GetRequiredService<ILogger<OllamaClient>>()));
}

// A named HTTP client with a light retry on 429/503 — polite to public, key-less APIs.
static void AddPoliteHttpClient(HostApplicationBuilder builder, string name, string baseUrl)
{
    builder.Services.AddHttpClient(name, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Grimoire/0.1 ( pmanso@go2chain.es )");
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddResilienceHandler(name, pipeline =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(2),
                ShouldHandle = static args => ValueTask.FromResult(
                    args.Outcome.Exception is HttpRequestException
                    || args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests }
                    || args.Outcome.Result is { StatusCode: HttpStatusCode.ServiceUnavailable }),
            });
        });
}
