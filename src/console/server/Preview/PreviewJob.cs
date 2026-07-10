using Grimoire.Library.Data;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Preview;

/// <summary>
/// Resolves audio previews and curated streaming links (features B13/B26, DECISIONS D10/D25).
/// iTunes is asked first and Deezer only complements it — never the other way round. The pass
/// is lazy and batched, never one big sweep: it processes only artists not yet attempted
/// (those with no <c>listen:</c> link), up to a per-run limit, so re-running resumes where it
/// left off. An artist with no match keeps <c>preview_url = null</c> — a real gap, since about
/// half the underground is genuinely inaudible.
/// </summary>
public sealed class PreviewJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyList<IEnrichmentSource> _sources;
    private readonly PreviewOptions _options;
    private readonly ILogger<PreviewJob> _logger;

    public PreviewJob(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IEnrichmentSource> sources,
        PreviewOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<PreviewJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _sources = sources.ToList();
        _options = options;
        _logger = logger;
    }

    protected override string CommandName => "Preview resolution";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        IEnrichmentSource? itunes = _sources.FirstOrDefault(s => s.Name == "iTunes" && s.Enabled);
        IEnrichmentSource? deezer = _sources.FirstOrDefault(s => s.Name == "Deezer" && s.Enabled);

        _logger.LogInformation(
            "Preview sources — iTunes: {ITunes}, Deezer: {Deezer}. Batch limit: {Limit}.",
            itunes is not null ? "on" : "off", deezer is not null ? "on" : "off", _options.Limit);

        // Candidates are the seeded corpus (tags or releases), not the bare member rows the
        // edges pass added. "Attempted" = already carries curated listen: links, so those are
        // excluded and the batch always advances.
        List<Artist> candidates = await db.Artists
            .Where(a => a.Tags.Length > 0 || a.Releases.Any())
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        List<Artist> pending = candidates
            .Where(a => a.Links is null || !a.Links.Keys.Any(k => k.StartsWith(StreamingLinks.Prefix, StringComparison.Ordinal)))
            .Take(_options.Limit)
            .ToList();

        _logger.LogInformation("{Pending} artists pending preview resolution (of {Total} candidates).",
            pending.Count, candidates.Count);

        int withPreview = 0;
        int attempted = 0;

        foreach (Artist artist in pending)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            ArtistEnrichment? apple = itunes is null ? null : await itunes.FetchAsync(artist, ct);
            ArtistEnrichment? dz = deezer is null ? null : await deezer.FetchAsync(artist, ct);

            // iTunes first, Deezer as complement (D25) — never the reverse.
            string? previewUrl = apple?.PreviewUrl ?? dz?.PreviewUrl;

            string? appleLink = apple is not null && apple.Links.TryGetValue(StreamingLinks.AppleMusicKey, out string? au) ? au : null;
            string? deezerLink = dz is not null && dz.Links.TryGetValue(StreamingLinks.DeezerKey, out string? du) ? du : null;

            MergeLinks(artist, StreamingLinks.Build(artist.Name, appleLink, deezerLink));
            artist.PreviewUrl = previewUrl;

            await db.SaveChangesAsync(ct);

            attempted++;

            if (previewUrl is not null)
            {
                withPreview++;
            }

            if (attempted % 20 == 0)
            {
                _logger.LogInformation("Attempted {Attempted}/{Total}, {WithPreview} with a preview.",
                    attempted, pending.Count, withPreview);
            }
        }

        double pct = attempted == 0 ? 0 : 100.0 * withPreview / attempted;

        _logger.LogInformation(
            "Preview batch complete: {WithPreview}/{Attempted} resolved a preview ({Pct:F1}%). Re-run to continue.",
            withPreview, attempted, pct);
    }

    private static void MergeLinks(Artist artist, IReadOnlyDictionary<string, string> curated)
    {
        // Additive merge: keep the raw MusicBrainz url-rels already present, add/refresh the
        // curated listen: links. A new dictionary instance makes the change detectable to EF.
        Dictionary<string, string> merged = artist.Links is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(artist.Links, StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> link in curated)
        {
            merged[link.Key] = link.Value;
        }

        artist.Links = merged;
    }
}
