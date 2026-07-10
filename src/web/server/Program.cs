using System.Text;
using System.Text.Json.Serialization;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Server.Auth;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
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

/// <summary>Exposed so the test host (WebApplicationFactory) can reference the entry point.</summary>
public partial class Program
{
}
