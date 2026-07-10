using System.Net;
using Grimoire.Library.Data;
using Grimoire.Worker;
using Grimoire.Worker.MusicBrainz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Serilog;
using Serilog.Events;

bool runSeed = args.Contains("seed", StringComparer.OrdinalIgnoreCase);

if (!runSeed)
{
    Console.WriteLine("Grimoire worker. Usage:");
    Console.WriteLine("  dotnet run --project src/console/server -- seed");
    Console.WriteLine();
    Console.WriteLine("The 'seed' verb fetches real artist data from MusicBrainz and upserts it into Postgres.");
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

int target = builder.Configuration.GetValue("Seed:Target", 300);

if (int.TryParse(Environment.GetEnvironmentVariable("GRIMOIRE_SEED_TARGET"), out int envTarget) && envTarget > 0)
{
    target = envTarget;
}

builder.Services.AddSingleton(new SeedOptions { RunSeed = true, Target = target });
builder.Services.AddSingleton<MusicBrainzRateLimiter>();

builder.Services.AddHttpClient<MusicBrainzClient>(client =>
    {
        client.BaseAddress = new Uri("https://musicbrainz.org/ws/2/");
        // MusicBrainz rejects requests without a descriptive User-Agent.
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

builder.Services.AddHostedService<MusicBrainzSeedJob>();

IHost host = builder.Build();
await host.RunAsync();
