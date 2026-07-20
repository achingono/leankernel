using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Abstraction for retrieving embedding vectors from text inputs.
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>
    /// Computes embeddings for the given text inputs.
    /// </summary>
    /// <param name="inputs">The text inputs to embed.</param>
    /// <param name="model">The embedding model identifier.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Embedding vectors in the same order as the inputs. Empty on failure.</returns>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GetEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        string model,
        CancellationToken cancellationToken = default);
}