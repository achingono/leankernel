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

    /// <summary>
    /// Represents the embedding service.
    /// </summary>
    public EmbeddingService(
        HttpClient httpClient,
        IOptions<LeanKernelConfig> config,
        ILogger<EmbeddingService> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// Executes the embed async operation.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var batch = await EmbedBatchAsync([text], ct);
        return batch[0];
    }

    /// <summary>
    /// Executes the embed batch async operation.
    /// </summary>
    /// <param name="texts">The texts.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct)
    {
        var textList = texts.ToList();
        if (textList.Count == 0) return [];

        try
        {
            var request = new EmbeddingRequest
            {
                Model = _config.LiteLlm.EmbeddingModel,
                Input = textList,
                Dimensions = _config.Qdrant.EmbeddingDimension
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
                .Select(d => FitToEmbeddingDimension(d.Embedding, _config.Qdrant.EmbeddingDimension))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding request failed — returning zero vectors");
            return textList.Select(_ => new float[_config.Qdrant.EmbeddingDimension]).ToList();
        }
    }

    private static float[] FitToEmbeddingDimension(float[] embedding, int targetDimension)
    {
        if (targetDimension <= 0 || embedding.Length == targetDimension)
        {
            return embedding;
        }

        if (embedding.Length > targetDimension)
        {
            return embedding.Take(targetDimension).ToArray();
        }

        var padded = new float[targetDimension];
        Array.Copy(embedding, padded, embedding.Length);
        return padded;
    }
}

// Source-generated JSON serialization for embedding API types
internal sealed record EmbeddingRequest
{
    [JsonPropertyName("model")] public required string Model { get; init; }
    [JsonPropertyName("input")] public required List<string> Input { get; init; }
    [JsonPropertyName("dimensions")] public int Dimensions { get; init; }
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

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
[JsonSerializable(typeof(EmbeddingRequest))]
[JsonSerializable(typeof(EmbeddingResponse))]
internal partial class EmbeddingJsonContext : JsonSerializerContext;

