using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Grimoire.Worker.Embedding;

/// <summary>
/// Minimal client for the self-hosted Ollama embeddings endpoint (Invariant 1 / DECISIONS
/// D6: zero operating cost, no paid model). Produces the 768-dim <c>nomic-embed-text</c>
/// vectors the discovery engine indexes. On failure it returns null and the caller leaves the
/// embedding unset rather than inventing one.
/// </summary>
public sealed class OllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<OllamaClient> _logger;

    public OllamaClient(HttpClient http, string model, ILogger<OllamaClient> logger)
    {
        _http = http;
        _model = model;
        _logger = logger;
    }

    /// <summary>Embeds one text. Returns the raw (un-centred) vector, or null on failure.</summary>
    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync(
            "api/embeddings",
            new EmbeddingRequest { Model = _model, Prompt = text },
            JsonOptions,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Ollama embeddings returned {Status}.", (int)response.StatusCode);
            return null;
        }

        EmbeddingResponse? body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOptions, ct);

        if (body?.Embedding is null || body.Embedding.Length == 0)
        {
            _logger.LogWarning("Ollama returned an empty embedding.");
            return null;
        }

        return body.Embedding;
    }

    private sealed class EmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;
    }

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
