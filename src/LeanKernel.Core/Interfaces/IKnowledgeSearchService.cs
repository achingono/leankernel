using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Read-only search service for the unified knowledge index (wiki + documents).
/// Queries Qdrant with agent-scoped tag filters.
/// </summary>
public interface IKnowledgeSearchService
{
    /// <summary>
    /// Search the knowledge index with agent-scoped filtering.
    /// </summary>
    /// <param name="query">Search text to embed and match against.</param>
    /// <param name="agentTags">Tags the agent is allowed to access. Use ["*"] for unrestricted.</param>
    /// <param name="limit">Maximum results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ranked list of matching knowledge chunks.</returns>
    Task<List<RelevanceScore>> SearchAsync(string query, IReadOnlyList<string> agentTags, int limit, CancellationToken ct);

    /// <summary>
    /// Check if the knowledge index is available and ready.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct);
}
