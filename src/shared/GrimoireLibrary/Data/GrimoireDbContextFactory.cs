using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Grimoire.Library.Data;

/// <summary>
/// Design-time factory so `dotnet ef` can build the model and generate migrations
/// against this class library without a running host. The connection string here is
/// only used at design time; the runtime hosts supply their own.
/// </summary>
public class GrimoireDbContextFactory : IDesignTimeDbContextFactory<GrimoireDbContext>
{
    public GrimoireDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("GRIMOIRE_CONNECTION")
            ?? "Host=localhost;Port=5433;Database=grimoire;Username=grimoire;Password=grimoire";

        DbContextOptionsBuilder<GrimoireDbContext> options = new();
        options.UseNpgsql(connectionString, o => o.UseVector()).UseSnakeCaseNamingConvention();

        return new GrimoireDbContext(options.Options);
    }
}
