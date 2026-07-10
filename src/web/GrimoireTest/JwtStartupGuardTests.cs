using System.Text;
using Grimoire.Server.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

// Run tests sequentially: these tests mutate process environment variables (the only
// configuration source that reaches WebApplicationBuilder.Configuration before the
// pre-Build guard runs), and parallel tests must not observe that mutation.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Grimoire.Tests;

/// <summary>
/// Verifies the fail-fast guard on the JWT signing key: outside Development the app must
/// refuse to boot with the committed dev key or any key shorter than 32 bytes, and must
/// boot with a strong key. Migration is disabled so no database is required.
///
/// Configuration precedence caveat (as warned): neither UseSetting (host config, sits
/// below appsettings.json) nor a WithWebHostBuilder ConfigureAppConfiguration source
/// (applied at host Build, which is AFTER the guard runs in top-level code) actually
/// reaches the guard. The environment variable source, added by CreateBuilder itself,
/// does — so the overrides go through Jwt__SigningKey / Grimoire__MigrateOnStartup, and
/// the strong-key test asserts the value landed before trusting the boot.
/// </summary>
public class JwtStartupGuardTests
{
    private const string DevDefaultKey = "dev-only-grimoire-signing-key-change-in-production-0123456789";
    private const string StrongKey = "unit-test-production-signing-key-0123456789-abcdef";

    private static WebApplicationFactory<Program> ProductionFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
    }

    [Fact]
    public void Production_WithDevDefaultKey_RefusesToBoot()
    {
        RunWithEnvironment(DevDefaultKey, () =>
        {
            using WebApplicationFactory<Program> factory = ProductionFactory();
            Exception? exception = Record.Exception(() => _ = factory.Services);

            Assert.NotNull(exception);
            Assert.Contains("SigningKey", FlattenMessages(exception!), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Production_WithShortKey_RefusesToBoot()
    {
        RunWithEnvironment("too-short", () =>
        {
            using WebApplicationFactory<Program> factory = ProductionFactory();
            Exception? exception = Record.Exception(() => _ = factory.Services);

            Assert.NotNull(exception);
            Assert.Contains("SigningKey", FlattenMessages(exception!), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Production_WithStrongKey_Boots()
    {
        RunWithEnvironment(StrongKey, () =>
        {
            using WebApplicationFactory<Program> factory = ProductionFactory();

            // Accessing Services builds the host; if the guard tripped this would throw.
            IOptions<JwtSettings> options = factory.Services.GetRequiredService<IOptions<JwtSettings>>();

            // Confirm the override actually landed (not the appsettings dev default), so
            // the successful boot is meaningful and not an artifact of a missed override.
            Assert.Equal(StrongKey, options.Value.SigningKey);
            Assert.True(Encoding.UTF8.GetByteCount(options.Value.SigningKey) >= 32);
        });
    }

    private static void RunWithEnvironment(string signingKey, Action body)
    {
        string? previousKey = Environment.GetEnvironmentVariable("Jwt__SigningKey");
        string? previousMigrate = Environment.GetEnvironmentVariable("Grimoire__MigrateOnStartup");

        try
        {
            Environment.SetEnvironmentVariable("Jwt__SigningKey", signingKey);
            Environment.SetEnvironmentVariable("Grimoire__MigrateOnStartup", "false");
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Jwt__SigningKey", previousKey);
            Environment.SetEnvironmentVariable("Grimoire__MigrateOnStartup", previousMigrate);
        }
    }

    private static string FlattenMessages(Exception exception)
    {
        List<string> messages = [];
        Exception? current = exception;

        while (current is not null)
        {
            messages.Add(current.Message);
            current = current.InnerException;
        }

        return string.Join(" | ", messages);
    }
}
