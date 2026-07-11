using System.Diagnostics;
using System.Text.Json;
using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Atlas;

/// <summary>
/// Projects the artist embeddings to 2D and writes <c>artists.xy_x</c>/<c>artists.xy_y</c> for
/// the Atlas (features C18/B22). The reduction runs offline and at zero cost (D6) via a pure
/// Python script (<c>scripts/atlas_project.py</c>): umap-learn, scikit-learn and numpy are all
/// absent here with no pip, so the script does a hand-rolled PCA in pure Python (documented
/// there). This job hands the script the embeddings, reads back the 2D coordinates and persists
/// them. Idempotent: it overwrites the coordinates each run, and the PCA is deterministic.
/// </summary>
public sealed class AtlasJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AtlasJob> _logger;

    public AtlasJob(
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        ILogger<AtlasJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override string CommandName => "Atlas projection";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        List<Artist> embedded = await db.Artists
            .Where(a => a.Embedding != null)
            .ToListAsync(ct);

        if (embedded.Count < 3)
        {
            _logger.LogWarning("Only {Count} embeddings present; run the embeddings pass first.", embedded.Count);
            return;
        }

        _logger.LogInformation("Projecting {Count} embeddings to 2D via {Script}...", embedded.Count, ScriptPath());

        string inputPath = Path.GetTempFileName();
        string outputPath = Path.GetTempFileName();

        try
        {
            await WriteInputAsync(inputPath, embedded, ct);

            if (!await RunScriptAsync(inputPath, outputPath, ct))
            {
                // The script failed and said why on stderr; do not invent coordinates.
                return;
            }

            Dictionary<string, double[]>? coords = await ReadOutputAsync(outputPath, ct);

            if (coords is null)
            {
                _logger.LogWarning("Atlas script produced no readable output; coordinates left unchanged.");
                return;
            }

            int written = Persist(embedded, coords);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Atlas complete: {Written} artists projected to 2D (xy_x/xy_y).", written);
        }
        finally
        {
            TryDelete(inputPath);
            TryDelete(outputPath);
        }
    }

    private static async Task WriteInputAsync(string path, List<Artist> artists, CancellationToken ct)
    {
        var payload = new
        {
            ids = artists.Select(a => a.Id.ToString()).ToList(),
            vectors = artists.Select(a => a.Embedding!.ToArray()).ToList(),
        };

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, cancellationToken: ct);
    }

    private async Task<bool> RunScriptAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "python3",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(ScriptPath());
        psi.ArgumentList.Add(inputPath);
        psi.ArgumentList.Add(outputPath);

        using Process? process = Process.Start(psi);

        if (process is null)
        {
            _logger.LogError("Could not start python3 for the Atlas projection.");
            return false;
        }

        string stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("Atlas script exited with code {Code}: {Stderr}", process.ExitCode, stderr.Trim());
            return false;
        }

        _logger.LogInformation("Atlas script: {Message}", stderr.Trim());
        return true;
    }

    private static async Task<Dictionary<string, double[]>?> ReadOutputAsync(string path, CancellationToken ct)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, double[]>>(stream, cancellationToken: ct);
    }

    private static int Persist(List<Artist> artists, Dictionary<string, double[]> coords)
    {
        int written = 0;

        foreach (Artist artist in artists)
        {
            if (coords.TryGetValue(artist.Id.ToString(), out double[]? xy) && xy.Length == 2)
            {
                artist.XyX = xy[0];
                artist.XyY = xy[1];
                written++;
            }
        }

        return written;
    }

    // The worker runs from the repo root (dotnet run --project src/console/server), so the
    // script sits at scripts/atlas_project.py. An env override keeps it deployable.
    private static string ScriptPath()
    {
        return Environment.GetEnvironmentVariable("GRIMOIRE_ATLAS_SCRIPT") ?? "scripts/atlas_project.py";
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete Atlas temp file {Path}.", path);
        }
    }
}
