namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Vector embedding abstraction — generates embeddings via LiteLLM or a local model.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates a vector embedding for a single text value.
    /// </summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
    /// <summary>
    /// Generates vector embeddings for multiple text values in a batch.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken ct);
}
