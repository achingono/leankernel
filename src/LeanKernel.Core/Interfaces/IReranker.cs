using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Re-scores retrieved candidates before final context assembly.
/// </summary>
public interface IReranker
{
    /// <summary>
    /// Rerank the supplied candidates for the given query.
    /// </summary>
    Task<IReadOnlyList<RelevanceScore>> RerankAsync(
        string query,
        IReadOnlyList<RelevanceScore> candidates,
        CancellationToken ct);
}

