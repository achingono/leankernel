namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Vector embedding abstraction — generates embeddings via LiteLLM or a local model.
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct);
}
