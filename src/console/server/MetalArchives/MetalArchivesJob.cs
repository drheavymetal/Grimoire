using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.MetalArchives;

/// <summary>
/// Matches catalogue bands on Metal Archives and imports the lyrical themes, MA genre and MA band id
/// (D48). The themes are the one field that exists nowhere else (Q4); the id gives every matched band
/// the Metallum link it owes them (Invariant 3). Runs under MA's agreed terms via
/// <see cref="MetalArchivesSource"/> (≤ 1 req/s, sequential, cached-by-marker, one pass — D42).
/// <para>
/// Scope: real bands with a discography (<c>Kind = Group</c> and at least one release). People,
/// orchestras and choirs are skipped — MA's band search would not resolve them, and after the
/// classical purge (supersedes D11/D13) there are no composers here to serve anyway. Resumable and
/// batched on the <see cref="Artist.MetalArchivesCheckedAt"/> marker: a checked band, matched or not,
/// is never fetched again, so a re-run continues where it left off and never re-crawls a miss.
/// </para>
/// </summary>
public sealed class MetalArchivesJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MetalArchivesSource _source;
    private readonly MetalArchivesOptions _options;
    private readonly ILogger<MetalArchivesJob> _logger;

    public MetalArchivesJob(
        IServiceScopeFactory scopeFactory,
        MetalArchivesSource source,
        MetalArchivesOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<MetalArchivesJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _source = source;
        _options = options;
        _logger = logger;
    }

    protected override string CommandName => "Metal Archives resolution";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        // Real bands with a discography, not yet checked. Metal Archives is metal-only, so a band whose
        // Last.fm tags place it firmly outside metal cannot match there — checking it just burns one of
        // MA's 1 req/s (the whole rate we are allowed, D42). We therefore skip bands that carry tags but
        // none reads metal-ish. An untagged band is an unknown, not a non-match, so it stays in the pool
        // (much of the underground has no Last.fm tags yet). Within the pool, ordered by listeners so the
        // bands people actually meet in the Rite get their themes first; nulls (unranked) sort last.
        List<Artist> pending = await db.Artists
            .Where(a => a.Kind == ArtistKind.Group
                && a.Releases.Any()
                && a.MetalArchivesCheckedAt == null
                && (a.Tags.Length == 0
                    || a.Tags.Any(t =>
                        EF.Functions.ILike(t, "%metal%")
                        || EF.Functions.ILike(t, "%thrash%")
                        || EF.Functions.ILike(t, "%doom%")
                        || EF.Functions.ILike(t, "%grind%")
                        || EF.Functions.ILike(t, "%sludge%")
                        || EF.Functions.ILike(t, "%djent%")
                        || EF.Functions.ILike(t, "%deathcore%")
                        || EF.Functions.ILike(t, "%mathcore%")
                        || EF.Functions.ILike(t, "%crust%")
                        || EF.Functions.ILike(t, "%powerviolence%"))))
            .OrderByDescending(a => a.Listeners ?? -1)
            .ThenBy(a => a.Name)
            .Take(_options.Limit)
            .ToListAsync(ct);

        _logger.LogInformation("{Pending} bands pending Metal Archives resolution.", pending.Count);

        int matched = 0;
        int withThemes = 0;
        int attempted = 0;

        foreach (Artist artist in pending)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            MetalArchivesBand? band = await _source.ResolveAsync(artist, ct);

            attempted++;

            // Mark checked either way (matched or a genuine miss) so a re-run never fetches it again.
            artist.MetalArchivesCheckedAt = DateTime.UtcNow;

            if (band is not null)
            {
                artist.MetalArchivesId = band.Id;
                artist.MetalArchivesGenre = band.Genre;
                artist.LyricalThemes = band.Themes;
                matched++;

                if (band.Themes.Length > 0)
                {
                    withThemes++;
                }
            }

            await db.SaveChangesAsync(ct);

            if (attempted % 25 == 0)
            {
                _logger.LogInformation(
                    "Attempted {Attempted}/{Total}, {Matched} matched, {WithThemes} with lyrical themes.",
                    attempted, pending.Count, matched, withThemes);
            }
        }

        _logger.LogInformation(
            "Metal Archives batch complete: {Matched}/{Attempted} matched, {WithThemes} carried themes. Re-run to continue.",
            matched, attempted, withThemes);
    }
}
