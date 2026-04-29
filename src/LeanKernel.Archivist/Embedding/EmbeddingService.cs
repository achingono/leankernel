using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Archivist.Embedding;

/// <summary>
/// Embedding service that calls LiteLLM's /embeddings endpoint
/// (OpenAI-compatible) to generate vector embeddings.
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(HttpClient httpClient, ILogger<EmbeddingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var batch = await EmbedBatchAsync([text], ct);
        return batch[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct)
    {
        // TODO: Phase 1 — call LiteLLM /v1/embeddings endpoint
        // For now, return zero vectors as placeholder
        _logger.LogDebug("EmbedBatchAsync called — returning placeholder vectors");
        var result = texts.Select(_ => new float[1536]).ToList();
        await Task.CompletedTask;
        return result;
    }
}
