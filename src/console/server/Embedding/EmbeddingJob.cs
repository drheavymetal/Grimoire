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

        List<Artist> artists = await db.Artists.ToListAsync(ct);
        Dictionary<Guid, string> nameById = artists.ToDictionary(a => a.Id, a => a.Name);

        // Members of a band: the "from" side of its member_of edges.
        Dictionary<Guid, List<string>> membersByBand = new();

        List<(Guid FromId, Guid ToId)> memberEdges = await db.ArtistEdges
            .Where(e => e.Kind == EdgeKind.MemberOf)
            .Select(e => new ValueTuple<Guid, Guid>(e.FromId, e.ToId))
            .ToListAsync(ct);

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

        // Phase 1: embed every artist that has signal, keeping the raw vectors in memory.
        List<(Artist Artist, float[] Raw)> raws = [];
        int embedded = 0;
        int skipped = 0;

        foreach (Artist artist in artists)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            membersByBand.TryGetValue(artist.Id, out List<string>? members);
            string? text = EmbeddingTextBuilder.Build(artist, members);

            if (text is null)
            {
                // No tags, abstract, place, members or label: no discovery signal, no vector.
                artist.Embedding = null;
                skipped++;
                continue;
            }

            float[]? vector = await _ollama.EmbedAsync(text, ct);

            if (vector is null)
            {
                continue;
            }

            if (vector.Length != _options.Dimensions)
            {
                _logger.LogWarning("Artist '{Name}': embedding had {Actual} dims, expected {Expected}; skipped.",
                    artist.Name, vector.Length, _options.Dimensions);
                continue;
            }

            raws.Add((artist, vector));
            embedded++;

            if (embedded % 50 == 0)
            {
                _logger.LogInformation("Embedded {Embedded} artists so far...", embedded);
            }
        }

        if (raws.Count == 0)
        {
            _logger.LogWarning("No embeddings produced; leaving corpus mean untouched.");
            return;
        }

        // Phase 2: centre on the corpus mean (D26) and persist both the centred vectors and
        // the mean itself, so the query vector can be centred with the identical vector later.
        float[] mean = VectorMath.Mean(raws.Select(r => r.Raw).ToList());

        foreach ((Artist artist, float[] raw) in raws)
        {
            artist.Embedding = new Vector(VectorMath.Subtract(raw, mean));
        }

        await PersistMeanAsync(db, mean, raws.Count, ct);

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Embedding complete: {Embedded} centred embeddings, {Skipped} artists skipped (no signal). Corpus mean persisted over {Count} vectors.",
            embedded, skipped, raws.Count);
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
