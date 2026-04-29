using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Archivist.Embedding;

/// <summary>
/// Embedding service that calls LiteLLM's /embeddings endpoint
/// (OpenAI-compatible) to generate vector embeddings.
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        HttpClient httpClient,
        IOptions<LeanKernelConfig> config,
        ILogger<EmbeddingService> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var batch = await EmbedBatchAsync([text], ct);
        return batch[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct)
    {
        var textList = texts.ToList();
        if (textList.Count == 0) return [];

        try
        {
            var request = new EmbeddingRequest
            {
                Model = _config.LiteLlm.EmbeddingModel,
                Input = textList
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/v1/embeddings",
                request,
                EmbeddingJsonContext.Default.EmbeddingRequest,
                ct);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync(
                EmbeddingJsonContext.Default.EmbeddingResponse,
                ct);

            if (result?.Data is null || result.Data.Count == 0)
            {
                _logger.LogWarning("Empty embedding response — returning zero vectors");
                return textList.Select(_ => new float[_config.Qdrant.EmbeddingDimension]).ToList();
            }

            return result.Data
                .OrderBy(d => d.Index)
                .Select(d => d.Embedding)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding request failed — returning zero vectors");
            return textList.Select(_ => new float[_config.Qdrant.EmbeddingDimension]).ToList();
        }
    }
}

// Source-generated JSON serialization for embedding API types
internal sealed record EmbeddingRequest
{
    [JsonPropertyName("model")] public required string Model { get; init; }
    [JsonPropertyName("input")] public required List<string> Input { get; init; }
}

internal sealed record EmbeddingResponse
{
    [JsonPropertyName("data")] public List<EmbeddingData> Data { get; init; } = [];
}

internal sealed record EmbeddingData
{
    [JsonPropertyName("index")] public int Index { get; init; }
    [JsonPropertyName("embedding")] public float[] Embedding { get; init; } = [];
}

[JsonSerializable(typeof(EmbeddingRequest))]
[JsonSerializable(typeof(EmbeddingResponse))]
internal partial class EmbeddingJsonContext : JsonSerializerContext;

