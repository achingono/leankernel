using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Retrieves knowledge candidates under an explicit retrieval scope.
/// </summary>
public interface IScopedKnowledgeService
{
    /// <summary>
    /// Retrieves scoped knowledge candidates and diagnostics for the supplied query.
    /// </summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="scope">The retrieval scope to enforce.</param>
    /// <param name="maxResults">The maximum number of results to request from the backing knowledge service.</param>
    /// <param name="sessionId">The session identifier for diagnostics.</param>
    /// <param name="turnId">The turn identifier for diagnostics.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The scoped retrieval result.</returns>
    Task<ScopedRetrievalResult> RetrieveWithScopeAsync(
        string query,
        string scope,
        int maxResults = 10,
        string? sessionId = null,
        string? turnId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Represents the results of a scoped retrieval operation.
/// </summary>
public sealed record ScopedRetrievalResult
{
    /// <summary>
    /// Gets the admitted retrieval candidates.
    /// </summary>
    public IReadOnlyList<RetrievalCandidate> Candidates { get; init; } = [];

    /// <summary>
    /// Gets the retrieval diagnostics for the operation.
    /// </summary>
    public RetrievalDiagnostics Diagnostics { get; init; } = null!;
}
