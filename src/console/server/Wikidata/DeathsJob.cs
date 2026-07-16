using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Wikidata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Wikidata;

/// <summary>
/// Populates <c>artists.death_date</c> and <c>artists.death_place</c> from Wikidata P570 (date of
/// death) and P20 (place of death), for people who carry a Wikidata QID (feature C12, In
/// Memoriam). Only the deceased come back from the query — P570 is required — so people with no
/// death on record simply keep their null fields. Only what Wikidata asserts is written; a
/// missing place stays null. Batched via a SPARQL <c>VALUES</c> clause. Idempotent: re-running
/// writes the same values.
/// </summary>
public sealed class DeathsJob : WorkerJob
{
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WikidataClient _client;
    private readonly ILogger<DeathsJob> _logger;

    public DeathsJob(
        IServiceScopeFactory scopeFactory,
        WikidataClient client,
        IHostApplicationLifetime lifetime,
        ILogger<DeathsJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _logger = logger;
    }

    protected override string CommandName => "Deaths import";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        // People only: a group does not die (C12). Kind filters in SQL; the 'wikidata' key lives in
        // a value-converted jsonb string that cannot be filtered in SQL, so the QID is read in
        // memory (the corpus is small).
        List<Artist> people = await db.Artists
            .Where(a => a.Kind == ArtistKind.Person)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        Dictionary<string, Artist> byQid = new(StringComparer.Ordinal);

        foreach (Artist person in people)
        {
            if (person.Links is not null
                && person.Links.TryGetValue("wikidata", out string? link)
                && WikidataQid.FromUri(link) is string qid)
            {
                byQid.TryAdd(qid, person);
            }
        }

        _logger.LogInformation("{Count} people carry a Wikidata QID. Querying P570/P20 in batches of {Batch}...",
            byQid.Count, BatchSize);

        if (byQid.Count == 0)
        {
            _logger.LogWarning("No people with a Wikidata QID; nothing to query.");
            return;
        }

        List<string> qids = [.. byQid.Keys];
        int withDate = 0;
        int withPlace = 0;
        int batches = 0;
        int deferred = 0;

        for (int i = 0; i < qids.Count; i += BatchSize)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            IEnumerable<string> batch = qids.Skip(i).Take(BatchSize);
            WikidataQueryResult result = await _client.QueryAsync(WikidataQueries.Deaths(batch), ct);
            batches++;

            if (!result.Answered)
            {
                // WDQS did not answer for these people. Nothing is written and nothing is stamped, so
                // a later run sweeps them again — an outage must never read as "nobody here died"
                // (D61). Counted so a run that quietly covered half the catalogue says so.
                deferred++;
                continue;
            }

            foreach (WikidataDeaths.Death death in WikidataDeaths.Parse(result.Response))
            {
                if (!byQid.TryGetValue(death.Qid, out Artist? artist))
                {
                    continue;
                }

                if (death.Date is DateOnly date)
                {
                    artist.DeathDate = date;
                    withDate++;
                }

                if (death.Place is not null)
                {
                    artist.DeathPlace = death.Place;
                    withPlace++;
                }
            }

            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Deaths complete: {Batches} batches, {WithDate} death dates and {WithPlace} places written, "
                + "{Deferred} batches deferred (no answer).",
            batches, withDate, withPlace, deferred);

        if (deferred > 0)
        {
            _logger.LogWarning(
                "{Deferred} batches went unanswered. Re-run to sweep them.",
                deferred);
        }
    }
}
