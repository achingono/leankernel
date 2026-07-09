using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Optional diagnostics sink capability for server-side filtered/paginated reads.
/// </summary>
public interface IDiagnosticsQuerySink : IDiagnosticsSink
{
    /// <summary>
    /// Gets filtered diagnostic entries for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="turnId">Optional turn filter.</param>
    /// <param name="limit">Optional max number of entries to return.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The filtered entries.</returns>
    Task<IReadOnlyList<DiagnosticEntry>> GetEntriesAsync(
        string sessionId,
        string? category,
        string? turnId,
        int? limit,
        CancellationToken ct = default);
}
