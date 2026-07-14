using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Auth;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Polly;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());

string connectionString = builder.Configuration.GetConnectionString("Grimoire")
    ?? throw new InvalidOperationException("Connection string 'Grimoire' is not configured.");

builder.Services.AddDbContext<GrimoireDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()).UseSnakeCaseNamingConvention());

// Identity (no cookies; JWT bearer only).
builder.Services.AddIdentityCore<GrimoireUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<GrimoireDbContext>();

JwtSettings jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("The 'Jwt' configuration section is missing.");

// Fail fast: never boot outside Development with the committed dev key or a key that is
// too short for HS256 (needs at least 256 bits of key material). Otherwise anyone who
// has read the repo could mint tokens. The key itself is never logged.
const string DevDefaultSigningKey = "dev-only-grimoire-signing-key-change-in-production-0123456789";
if (!builder.Environment.IsDevelopment())
{
    bool isDevDefault = jwtSettings.SigningKey == DevDefaultSigningKey;
    bool isTooShort = Encoding.UTF8.GetByteCount(jwtSettings.SigningKey) < 32;

    if (isDevDefault || isTooShort)
    {
        throw new InvalidOperationException(
            "Refusing to start: Jwt:SigningKey is the committed dev default or shorter than 32 bytes. "
            + "Set the environment variable Jwt__SigningKey to a random value of at least 32 bytes "
            + "(256 bits) before running outside Development.");
    }
}

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<TokenService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

const string CorsPolicy = "GrimoireFront";
string[] allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
    options.AddPolicy(CorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// Cover Art Archive proxy (feature B6): a typed HttpClient plus an on-disk cache.
builder.Services.Configure<CoverCacheOptions>(builder.Configuration.GetSection("CoverCache"));
builder.Services.AddHttpClient<CoverArtCache>(client =>
{
    client.BaseAddress = new Uri("https://coverartarchive.org/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Grimoire/0.1 ( pmanso@go2chain.es )");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// The Rite (movement II). Artist detail is shared by the artist page and the reveal.
builder.Services.AddScoped<ArtistDetailBuilder>();

// Lineage (movement IV): loads the artist graph for the Bloodline, Six Degrees, diaspora,
// Rabbit Hole and grimoire-graph endpoints.
builder.Services.AddScoped<LineageGraph>();

// The Atlas (movement VI, C18/B22): reconstructs and caches the offline PCA basis so a live taste
// vector can be placed on the same 2D map as the stored star field.
builder.Services.AddSingleton<AtlasProjector>();

// Web Push for the Weekly Rite (movement VI, B17). The VAPID public key ships in config; the
// PRIVATE key lives only in user-secrets / an env var and is never committed. Sending is disabled
// (notify → 503) when the private key is absent, so the plumbing degrades honestly (DECISIONS D28).
WebPushOptions webPushOptions = builder.Configuration.GetSection("WebPush").Get<WebPushOptions>()
    ?? new WebPushOptions();
builder.Services.AddSingleton(webPushOptions);
builder.Services.AddSingleton<WebPushSender>();

// The discovery engine and its tunables (percentile ring — DECISIONS D26).
RiteEngineOptions riteOptions = builder.Configuration.GetSection("Rite").Get<RiteEngineOptions>()
    ?? new RiteEngineOptions();
builder.Services.AddSingleton(riteOptions);
builder.Services.AddScoped<RiteEngine>();

// Audio proxy (SPEC §5.3): streams previews server-side, never leaking the origin URL. SSRF is
// closed off by an allow-list; no redirects are followed onto some other host.
builder.Services.AddHttpClient<PreviewAudioProxy>(client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Grimoire/0.1 ( pmanso@go2chain.es )");
        client.Timeout = TimeSpan.FromSeconds(20);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Just-in-time preview resolution (DECISIONS D25/D19): at 207k artists the Rite cannot pre-resolve a
// preview for the whole catalogue under the iTunes ceiling, so the URL is resolved at serve time from
// the free iTunes and Deezer search APIs (iTunes first, Deezer as complement — never the reverse) and
// cached on artists.preview_url. Two named clients with short timeouts and a retry on transient
// failures / 429; the resolver itself paces the calls. A singleton so the pacing gates are shared.
builder.Services.AddHttpClient(PreviewResolver.ITunesClientName, client =>
    {
        client.BaseAddress = new Uri("https://itunes.apple.com/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Grimoire/0.1 ( pmanso@go2chain.es )");
        client.Timeout = TimeSpan.FromSeconds(6);
    })
    .AddResilienceHandler("preview-itunes", ConfigurePreviewRetry);
builder.Services.AddHttpClient(PreviewResolver.DeezerClientName, client =>
    {
        client.BaseAddress = new Uri("https://api.deezer.com/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Grimoire/0.1 ( pmanso@go2chain.es )");
        client.Timeout = TimeSpan.FromSeconds(6);
    })
    .AddResilienceHandler("preview-deezer", ConfigurePreviewRetry);
builder.Services.AddSingleton<PreviewResolver>();

// Semantic search (feature B2): embeds a free-text query with the same self-hosted nomic-embed-text
// the ETL indexed with, then centres it by the stored corpus mean (D26/D31). Unreachable → 503.
OllamaOptions ollamaOptions = builder.Configuration.GetSection("Ollama").Get<OllamaOptions>()
    ?? new OllamaOptions();
builder.Services.AddSingleton(ollamaOptions);
builder.Services.AddHttpClient<OllamaEmbedder>(client =>
{
    client.BaseAddress = new Uri(ollamaOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Last.fm cold start (feature C1). Disabled while no API key is configured (blocker Q5); the
// endpoint then reports the gap rather than inventing scrobbles.
LastFmOptions lastFmOptions = builder.Configuration.GetSection("LastFm").Get<LastFmOptions>()
    ?? new LastFmOptions();
builder.Services.AddSingleton(lastFmOptions);
builder.Services.AddHttpClient<IColdStartImport, LastFmColdStart>(client =>
{
    client.BaseAddress = new Uri("https://ws.audioscrobbler.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Grimoire API", Version = "v1" });

    OpenApiSecurityScheme scheme = new()
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT access token.",
    };
    options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, scheme);
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, null)] = new List<string>(),
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<GrimoireDbContext>();

WebApplication app = builder.Build();

// Apply migrations on startup (same convention as qlaios), unless disabled for tests.
if (app.Configuration.GetValue("Grimoire:MigrateOnStartup", true))
{
    using IServiceScope scope = app.Services.CreateScope();
    GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
    await db.Database.MigrateAsync();
}

// In production the API sits behind Traefik, which terminates TLS and forwards over plain http.
// Without this, Request.Scheme is "http" and the capability audio URLs the Rite hands out (built
// from Request.Scheme/Host) come back as http:// on an https:// page — the browser blocks them as
// mixed content and the blind listen dies. The proxy is on the docker network, so the known
// networks/proxies allowlist is cleared: only the edge ever reaches this port.
ForwardedHeadersOptions forwardedHeaders = new()
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedFor,
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();

// Retry for the JIT preview clients: a few jittered exponential retries on a transient network error
// or a 429/503 from the free search APIs, so a stray throttle does not sink a serve. Kept modest —
// the resolver already paces the calls, and an unresolved preview is a legitimate outcome, not a fault.
static void ConfigurePreviewRetry(ResiliencePipelineBuilder<HttpResponseMessage> pipeline)
{
    pipeline.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromMilliseconds(400),
        ShouldHandle = static args => ValueTask.FromResult(
            args.Outcome.Exception is HttpRequestException
            || args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests }
            || args.Outcome.Result is { StatusCode: HttpStatusCode.ServiceUnavailable }),
    });
}

/// <summary>Exposed so the test host (WebApplicationFactory) can reference the entry point.</summary>
public partial class Program
{
}
