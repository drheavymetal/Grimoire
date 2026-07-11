using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Wikidata;
using Grimoire.Worker.Credits;
using Grimoire.Worker.MusicBrainz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.PersonLinks;

/// <summary>
/// Enriches the minimal member rows (people inserted by the edges pass with identity only) with
/// their MusicBrainz url-rels, so they gain external links — above all the Wikidata QID that the
/// deaths pass (C12 In Memoriam) needs and could not find. Runs one <c>inc=url-rels</c> lookup per
/// person at 1 req/s, merges the links additively (never dropping links they already carry), and
/// is <b>batched</b> (<see cref="PersonLinksOptions.Limit"/>), <b>resumable</b> (a disk ledger
/// skips people already attempted) and <b>idempotent</b>. After it runs, re-run the <c>deaths</c>
/// verb to populate In Memoriam. Nothing is invented: a person MusicBrainz gives no links keeps a
/// null <c>links</c>.
/// </summary>
public sealed class PersonLinksJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MusicBrainzClient _client;
    private readonly EtlCache _cache;
    private readonly PersonLinksOptions _options;
    private readonly ILogger<PersonLinksJob> _logger;

    public PersonLinksJob(
        IServiceScopeFactory scopeFactory,
        MusicBrainzClient client,
        EtlCache cache,
        PersonLinksOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<PersonLinksJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    protected override string CommandName => "Person links import";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        List<Artist> people = await db.Artists
            .Where(a => a.Kind == ArtistKind.Person && a.Mbid != Guid.Empty)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        ProgressLedger ledger = _cache.Ledger("personlinks");

        List<Artist> pending = people.Where(p => !ledger.Contains(p.Mbid)).ToList();
        List<Artist> batch = pending.Take(_options.Limit).ToList();

        _logger.LogInformation(
            "Person links: {Total} people, {Done} attempted, {Pending} pending. Processing {Batch} this pass at 1 req/s.",
            people.Count, ledger.Count, pending.Count, batch.Count);

        int gainedLinks = 0;
        int gainedQid = 0;

        foreach (Artist person in batch)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            MbArtist? detail = await _client.GetArtistLinksAsync(person.Mbid.ToString(), ct);

            Dictionary<string, string>? fetched = MbMapping.MapLinks(detail?.Relations);

            if (fetched is not null)
            {
                bool hadQid = HasWikidata(person.Links);

                person.Links = Merge(person.Links, fetched);
                gainedLinks++;

                if (!hadQid && HasWikidata(person.Links))
                {
                    gainedQid++;
                }

                await db.SaveChangesAsync(ct);
            }

            await ledger.MarkAsync(person.Mbid, ct);
        }

        int remaining = pending.Count - batch.Count;

        _logger.LogInformation(
            "Person links complete: {Batch} people processed, {Links} gained links, {Qid} newly carry a Wikidata QID. "
            + "{Remaining} people still pending. Re-run 'deaths' to populate In Memoriam.",
            batch.Count, gainedLinks, gainedQid, remaining);
    }

    private static bool HasWikidata(Dictionary<string, string>? links)
    {
        return links is not null
            && links.TryGetValue("wikidata", out string? link)
            && WikidataQid.FromUri(link) is not null;
    }

    private static Dictionary<string, string> Merge(Dictionary<string, string>? existing, Dictionary<string, string> fetched)
    {
        Dictionary<string, string> merged = existing is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        foreach ((string key, string value) in fetched)
        {
            merged[key] = value;
        }

        return merged;
    }
}
