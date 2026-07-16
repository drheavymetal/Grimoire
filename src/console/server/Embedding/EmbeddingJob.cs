using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace Grimoire.Worker.Embedding;

/// <summary>
/// Builds centred text embeddings for the discovery engine (DECISIONS D26, variant C). For
/// each artist with signal it composes rich text (name, tags, country, members, label,
/// abstract), embeds it with self-hosted <c>nomic-embed-text</c>, then subtracts the corpus
/// mean before storing — the step that triples near/far separation and makes the ring search
/// (D4) mean something. The mean is persisted in <c>corpus_stats</c> so the query side can
/// subtract the very same vector from the user's taste. Bare member rows carry no signal and
/// keep a null embedding; nothing is invented. Idempotent: recomputes and overwrites.
/// </summary>
public sealed class EmbeddingJob : WorkerJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OllamaClient _ollama;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<EmbeddingJob> _logger;

    public EmbeddingJob(
        IServiceScopeFactory scopeFactory,
        OllamaClient ollama,
        EmbeddingOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<EmbeddingJob> logger)
        : base(lifetime, logger)
    {
        _scopeFactory = scopeFactory;
        _ollama = ollama;
        _options = options;
        _logger = logger;
    }

    protected override string CommandName => "Embedding pass";

    // A corpus mean persisted with at least this many artists is treated as a real catalogue-scale
    // mean (D26) — the marker that lets a resume reuse it instead of clearing and starting over.
    private const int MeanSampleTarget = 6_000;
    private const int BatchSize = 400;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("Embedding source disabled by configuration; nothing to do.");
            return;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        GrimoireDbContext db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();

        await db.Database.MigrateAsync(ct);

        // Member-name map for the embedding text (read-only, untracked so it costs little memory).
        Dictionary<Guid, List<string>> membersByBand = await BuildMembersMapAsync(db, ct);

        int embeddedNow = await db.Artists.CountAsync(a => a.Embedding != null, ct);

        // Establish the corpus mean (D26). A catalogue-scale mean is marked by persisting it with
        // ArtistCount >= MeanSampleTarget. If one exists we RESUME on it (fill the remaining null
        // vectors) — crucially, even mid-rebuild after a kill, so a restart never re-clears progress.
        // Only when there is no catalogue mean (fresh catalogue, or the old tiny bootstrap mean) do
        // we recompute from a sample and clear stale vectors so all re-centre on one mean.
        CorpusStat? stat = await db.CorpusStats.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == CorpusStat.SingletonId, ct);
        bool haveCatalogueMean = stat?.MeanEmbedding is not null && stat.ArtistCount >= MeanSampleTarget;

        float[] mean;
        if (!haveCatalogueMean)
        {
            _logger.LogInformation(
                "Fresh embedding rebuild ({Existing} existing): computing the corpus mean from a sample of {Sample}.",
                embeddedNow, MeanSampleTarget);

            float[]? sampleMean = await ComputeSampleMeanAsync(db, membersByBand, ct);
            if (sampleMean is null)
            {
                _logger.LogWarning("No sample vectors produced; leaving the catalogue untouched.");
                return;
            }

            mean = sampleMean;
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE artists SET embedding = NULL, embedding_fingerprint = NULL", ct);
            // Persist with the sample size as the "catalogue mean exists" marker, so a later resume
            // (after a kill mid-fill) reuses this mean instead of clearing and starting over.
            await PersistMeanAsync(db, mean, MeanSampleTarget, ct);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        else
        {
            mean = stat!.MeanEmbedding!.ToArray();
            _logger.LogInformation("Resuming embedding pass ({Existing} already centred) with the persisted mean.", embeddedNow);
        }

        // Main loop: keyset-page over EVERY artist, embed + centre + SAVE per batch. Batched saves
        // make this resumable and memory-bounded — a kill loses at most one batch.
        //
        // The pass walks the whole table, not just `embedding IS NULL`, because a vector being
        // present never meant it was still true: enrichment keeps rewriting the text underneath it
        // (Last.fm tags, a Wikipedia biography, a newly-linked member), and under the old filter
        // those artists were fixed at whatever they looked like the day they were first embedded —
        // there was no way to ask for a re-embed short of clearing the catalogue. The fingerprint
        // makes the question cheap and exact: rebuild the text (pure CPU, no Ollama), compare, and
        // only call the model when it actually moved. A steady-state run over 200k rows costs one
        // table scan and no inference at all.
        Guid last = Guid.Empty;
        int done = 0;
        int noSignal = 0;
        int unchanged = 0;

        while (!ct.IsCancellationRequested)
        {
            List<Artist> page = await db.Artists
                .FromSqlInterpolated(
                    $"SELECT * FROM artists WHERE id > {last} ORDER BY id LIMIT {BatchSize}")
                .ToListAsync(ct);

            if (page.Count == 0)
            {
                break;
            }

            foreach (Artist artist in page)
            {
                last = artist.Id;
                membersByBand.TryGetValue(artist.Id, out List<string>? members);
                string? text = EmbeddingTextBuilder.Build(artist, members);

                if (text is null)
                {
                    // No signal: it stays null by design (D26). We have already moved `last` past it.
                    noSignal++;
                    continue;
                }

                string fingerprint = EmbeddingTextBuilder.Fingerprint(text);

                if (artist.Embedding is not null && artist.EmbeddingFingerprint == fingerprint)
                {
                    // The stored vector was built from exactly this text: still true, leave it.
                    unchanged++;
                    continue;
                }

                float[]? raw = await _ollama.EmbedAsync(text, ct);
                if (raw is null || raw.Length != _options.Dimensions)
                {
                    continue;
                }

                // Centred on the PERSISTED mean, never a freshly recomputed one. Every user's taste
                // vector (D33) is an average of vectors centred on that mean, so moving the mean
                // would silently leave every stored taste and repulsion in a different space than
                // the catalogue they are compared against — the ring search would quietly go wrong.
                // The mean is a fixed reference point (D26/D31), not a statistic to keep current.
                artist.Embedding = new Vector(VectorMath.Subtract(raw, mean));
                artist.EmbeddingFingerprint = fingerprint;
                done++;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            _logger.LogInformation(
                "Embedded {Done} this pass ({Unchanged} already current, {NoSignal} no-signal)...",
                done, unchanged, noSignal);
        }

        int finalCount = await db.Artists.CountAsync(a => a.Embedding != null, ct);
        await PersistMeanAsync(db, mean, finalCount, ct);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Embedding pass complete: {Count} centred embeddings in the catalogue.", finalCount);
    }

    // The member names of each band (the "from" side of its member_of edges), for the embedding text.
    private static async Task<Dictionary<Guid, List<string>>> BuildMembersMapAsync(GrimoireDbContext db, CancellationToken ct)
    {
        Dictionary<Guid, string> nameById = await db.Artists
            .AsNoTracking()
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        List<(Guid FromId, Guid ToId)> memberEdges = await db.ArtistEdges
            .AsNoTracking()
            .Where(e => e.Kind == EdgeKind.MemberOf)
            .Select(e => new ValueTuple<Guid, Guid>(e.FromId, e.ToId))
            .ToListAsync(ct);

        Dictionary<Guid, List<string>> membersByBand = new();
        foreach ((Guid fromId, Guid toId) in memberEdges)
        {
            if (nameById.TryGetValue(fromId, out string? memberName))
            {
                if (!membersByBand.TryGetValue(toId, out List<string>? list))
                {
                    list = [];
                    membersByBand[toId] = list;
                }

                list.Add(memberName);
            }
        }

        return membersByBand;
    }

    // The corpus mean (D26) estimated from a random sample of tagged artists — a stable
    // approximation of the full-catalogue mean, so batches can be centred without holding every
    // raw vector in memory. Any fixed vector keeps artist and query sides consistent; the sample
    // mean also recovers most of the near/far separation the full mean would.
    private async Task<float[]?> ComputeSampleMeanAsync(
        GrimoireDbContext db,
        Dictionary<Guid, List<string>> membersByBand,
        CancellationToken ct)
    {
        List<Artist> sample = await db.Artists
            .FromSqlInterpolated(
                $"SELECT * FROM artists WHERE cardinality(tags) > 0 ORDER BY random() LIMIT {MeanSampleTarget}")
            .AsNoTracking()
            .ToListAsync(ct);

        List<float[]> raws = [];
        foreach (Artist artist in sample)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            membersByBand.TryGetValue(artist.Id, out List<string>? members);
            string? text = EmbeddingTextBuilder.Build(artist, members);
            if (text is null)
            {
                continue;
            }

            float[]? raw = await _ollama.EmbedAsync(text, ct);
            if (raw is not null && raw.Length == _options.Dimensions)
            {
                raws.Add(raw);
            }

            if (raws.Count > 0 && raws.Count % 1000 == 0)
            {
                _logger.LogInformation("Corpus-mean sample: {Count} vectors...", raws.Count);
            }
        }

        return raws.Count == 0 ? null : VectorMath.Mean(raws);
    }

    private static async Task PersistMeanAsync(GrimoireDbContext db, float[] mean, int count, CancellationToken ct)
    {
        CorpusStat? stat = await db.CorpusStats.FirstOrDefaultAsync(c => c.Id == CorpusStat.SingletonId, ct);

        if (stat is null)
        {
            stat = new CorpusStat { Id = CorpusStat.SingletonId };
            db.CorpusStats.Add(stat);
        }

        stat.MeanEmbedding = new Vector(mean);
        stat.ArtistCount = count;
        stat.ComputedAt = DateTimeOffset.UtcNow;
    }
}
