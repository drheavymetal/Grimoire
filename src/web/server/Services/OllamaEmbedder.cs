using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grimoire.Server.Services;

/// <summary>Options for reaching the self-hosted Ollama embeddings endpoint (Invariant 1 / D6).</summary>
public sealed class OllamaOptions
{
    /// <summary>Base URL of the Ollama server. Defaults to the local dev instance.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/";

    /// <summary>Embedding model. Must match the one the ETL indexed with (nomic-embed-text, 768 dims).</summary>
    public string Model { get; set; } = "nomic-embed-text";
}

/// <summary>
/// Embeds a free-text query for semantic search (B2) with the same self-hosted
/// <c>nomic-embed-text</c> model the ETL indexed the corpus with, and with the same request shape
/// (a bare <c>prompt</c>, no task prefix) so the query lands in the same space. The corpus mean is
/// subtracted downstream to centre it (D26/D31 — the stored mean exists precisely to centre an
/// external raw query vector). Returns null on failure so the caller reports the gap (503) instead
/// of inventing a vector.
/// </summary>
public sealed class OllamaEmbedder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly OllamaOptions _options;

    public OllamaEmbedder(HttpClient http, OllamaOptions options)
    {
        _http = http;
        _options = options;
    }

    /// <summary>Embeds one query. Returns the raw (un-centred) vector, or null when Ollama is unreachable.</summary>
    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync(
                "api/embeddings",
                new EmbeddingRequest { Model = _options.Model, Prompt = text },
                JsonOptions,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            EmbeddingResponse? body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOptions, ct);

            if (body?.Embedding is null || body.Embedding.Length == 0)
            {
                return null;
            }

            return body.Embedding;
        }
        catch (HttpRequestException)
        {
            // Ollama not reachable: the caller turns this into an honest 503, never a faked result.
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out talking to Ollama (as opposed to the request being cancelled): same honest gap.
            return null;
        }
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
