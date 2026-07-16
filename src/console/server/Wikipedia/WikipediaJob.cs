using Grimoire.Library.Data;
using Grimoire.Library.Enrichment;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Wikipedia;

/// <summary>
/// Fills artists' Wikipedia biographies (and their article URLs, for CC BY-SA attribution) in every
/// configured edition, matched accurately through the MusicBrainz id → Wikidata (<c>wdt:P434</c>) →
/// Wikipedia bridge (never by name — homonyms are the trap). Most bands have no biography today;
/// this pass closes that gap for the ones that have an article, in whatever language they have it in.
/// <para>
/// English lands on <see cref="Artist.Abstract"/>, every other language on an
/// <see cref="ArtistBiography"/> row — see that type for why the split exists and
/// <see cref="ArtistBiographies"/> for the seam that hides it. Which editions to ask for is
/// <see cref="WikipediaOptions.Languages"/>: adding one is configuration, not schema.
/// </para>
/// <para>
/// Scope: artists that carry a MusicBrainz id and are unchecked <b>in at least one</b> configured
/// language, worked most-popular-first (Known bands actually have articles; the underground mostly
/// will not match, so the ones that pay off go first). Resumable per language: a checked artist,
/// matched or not, is stamped for that language alone, so a re-run never re-queries a miss and adding
/// a language walks the corpus for that language without disturbing the others.
/// </para>
/// <para>
/// Note: filling <see cref="Artist.Abstract"/> changes the text the embedding pass builds its vector
/// from, so English matches should be <b>re-embedded</b> later; D62's fingerprint makes that pass
/// notice on its own. This job deliberately does <b>not</b> trigger it. The other languages never
/// touch the vector at all, by design — <c>nomic-embed-text</c> is trained on English, so letting a
/// Spanish abstract into that text would move a band across the map for a linguistic reason rather
/// than a musical one.
/// </para>
/// </summary>
public sealed class WikipediaJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WikipediaSource _source;
    private readonly WikipediaOptions _options;
    private readonly ILogger<WikipediaJob> _logger;

    public WikipediaJob(
        IServiceScopeFactory scopeFactory,
        WikipediaSource source,
        WikipediaOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<WikipediaJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _source = source;
        _options = options;
        _logger = logger;
    }

    protected override string CommandName => "Wikipedia biography resolution";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        List<string> languages = [.. _options.Languages];

        if (languages.Count == 0)
        {
            _logger.LogWarning("No Wikipedia languages configured; nothing to do.");
            return;
        }

        _logger.LogInformation("Resolving Wikipedia biographies in: {Languages}.", string.Join(", ", languages));

        List<Artist> pending = await PendingAsync(db, languages, ct);

        _logger.LogInformation("{Pending} artists pending Wikipedia biography resolution.", pending.Count);

        Dictionary<string, Tally> tallies = languages.ToDictionary(l => l, _ => new Tally(), StringComparer.OrdinalIgnoreCase);
        int done = 0;

        // Chunk into one WDQS query per BatchSize MBIDs — the throughput win over one query per artist.
        foreach (Artist[] batch in pending.Chunk(_options.BatchSize))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            List<BiographyRequest> requests = batch
                .Select(a => new BiographyRequest(a, ArtistBiographies.PendingLanguages(a, languages)))
                .ToList();

            IReadOnlyDictionary<Guid, BiographySet> results = await _source.ResolveBatchAsync(requests, ct);

            foreach (BiographyRequest request in requests)
            {
                if (!results.TryGetValue(request.Artist.Mbid, out BiographySet? set))
                {
                    continue;
                }

                foreach (string language in request.Languages)
                {
                    if (set.For(language) is { } result)
                    {
                        Apply(request.Artist, language, result, tallies[language]);
                    }
                }
            }

            done += batch.Length;

            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Attempted {Done}/{Total} artists. {Tallies}", done, pending.Count, Describe(tallies));
        }

        _logger.LogInformation(
            "Wikipedia batch complete over {Done} artists. {Tallies} Re-run to continue.",
            done, Describe(tallies));
    }

    /// <summary>
    /// The artists still owed a lookup in at least one configured language, most-popular-first. The
    /// rule itself is <see cref="ArtistBiographies.PendingPredicate"/> — it lives next to its
    /// in-memory twin, <see cref="ArtistBiographies.PendingLanguages"/>, because the two disagreeing
    /// is what an infinite loop looks like here (D61).
    /// </summary>
    private async Task<List<Artist>> PendingAsync(GrimoireDbContext db, List<string> languages, CancellationToken ct)
    {
        return await db.Artists
            .Include(a => a.Biographies)
            .Where(ArtistBiographies.PendingPredicate(languages))
            // Ordered by listeners so the bands people actually meet get their biography first; the
            // underground (mostly unmatched) sorts last but is still attempted once, then stamped.
            .OrderByDescending(a => a.Listeners ?? -1)
            .ThenBy(a => a.Name)
            .Take(_options.Limit)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Records one language's outcome for one artist. The three cases ARE D61: a hit stores and
    /// stamps, a definitive miss stamps with nothing (so a re-run never asks again), and a transient
    /// failure writes <b>nothing at all</b> — leaving the artist unstamped in this language so a later
    /// run retries. Stamping on <see cref="EnrichmentOutcome.Unavailable"/> is what records a timeout
    /// forever as "this band has no biography".
    /// </summary>
    private static void Apply(Artist artist, string language, BiographyResult result, Tally tally)
    {
        if (result.Outcome == EnrichmentOutcome.Unavailable)
        {
            tally.Unavailable++;
            return;
        }

        WikipediaBiography? biography = result.Outcome == EnrichmentOutcome.Matched ? result.Biography : null;

        if (ArtistBiographies.IsEnglish(language))
        {
            if (biography is not null)
            {
                artist.Abstract = biography.Abstract;
                artist.AbstractUrl = biography.Url;
            }

            artist.AbstractCheckedAt = DateTime.UtcNow;
        }
        else
        {
            // The row itself is this language's marker, so it is written for a miss too — with no
            // text, which is an honest recorded gap rather than an invented biography (Invariant 5).
            artist.Biographies.Add(new ArtistBiography
            {
                ArtistId = artist.Id,
                Language = language.ToLowerInvariant(),
                Abstract = biography?.Abstract,
                AbstractUrl = biography?.Url,
                CheckedAt = DateTime.UtcNow,
            });
        }

        tally.Attempted++;

        if (biography is not null)
        {
            tally.Matched++;
        }
    }

    private static string Describe(Dictionary<string, Tally> tallies) =>
        string.Join(
            " ",
            tallies.Select(t =>
                $"[{t.Key}: {t.Value.Matched} matched, {t.Value.Attempted} checked, {t.Value.Unavailable} left for retry]"));

    /// <summary>Per-language counters for one run. A class, not a struct: it is mutated through a dictionary.</summary>
    private sealed class Tally
    {
        public int Matched { get; set; }

        public int Attempted { get; set; }

        public int Unavailable { get; set; }
    }
}
