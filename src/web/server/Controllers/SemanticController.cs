using Grimoire.Library.Data;
using Grimoire.Library.Models;
using Grimoire.Library.Services;
using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Movement V — semantic search (B2). "Something like Neurosis but slower": a free-text query is
/// embedded with the same self-hosted <c>nomic-embed-text</c> model that indexed the corpus,
/// centred by subtracting the stored corpus mean (D26/D31 — the mean exists to bring an external
/// raw query into the indexed frame), then matched against the HNSW index by cosine distance.
///
/// <para>
/// This is trigram search's opposite: it does not care how the words are spelled, it cares what
/// they mean. When Ollama is unreachable the endpoint returns 503 rather than a faked ranking; a
/// query with no signal simply returns nothing.
/// </para>
/// </summary>
[ApiController]
[Route("api/semantic")]
public class SemanticController : ControllerBase
{
    private readonly GrimoireDbContext _db;
    private readonly OllamaEmbedder _embedder;

    public SemanticController(GrimoireDbContext db, OllamaEmbedder embedder)
    {
        _db = db;
        _embedder = embedder;
    }

    /// <summary>
    /// Semantically nearest artists to a free-text query (B2), nearest first. 503 when the embedding
    /// service is unreachable; empty when the query is blank.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SemanticHitDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<SemanticHitDto>());
        }

        int take = Math.Clamp(limit, 1, 50);

        float[]? raw = await _embedder.EmbedAsync(q.Trim(), ct);

        if (raw is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "The semantic engine is unavailable right now. The embedding service did not answer.",
            });
        }

        // Centre the raw query with the stored corpus mean so it shares the frame of the indexed,
        // already-centred embeddings (D26/D31). Without the mean the query lives in a different frame.
        CorpusStat? stats = await _db.CorpusStats
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == CorpusStat.SingletonId, ct);

        if (stats?.MeanEmbedding is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "The corpus is not centred yet, so semantic search cannot place the query.",
            });
        }

        float[] mean = stats.MeanEmbedding.ToArray();

        if (mean.Length != raw.Length)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "The query embedding does not match the indexed dimensionality.",
            });
        }

        Vector centred = new(VectorMath.Subtract(raw, mean));

        List<SemanticHitDto> hits = await _db.Artists
            .AsNoTracking()
            .Discoverable()
            .OrderBy(a => a.Embedding!.CosineDistance(centred))
            .Take(take)
            .Select(a => new SemanticHitDto(
                a.Id,
                a.Name,
                a.Country,
                a.FormedYear,
                a.Rank,
                a.Embedding!.CosineDistance(centred)))
            .ToListAsync(ct);

        return Ok(hits);
    }
}
