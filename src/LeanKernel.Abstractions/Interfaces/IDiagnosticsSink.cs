using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Sink for recording and querying diagnostic entries.
/// </summary>
public interface IDiagnosticsSink
{
    /// <summary>
    /// Records a diagnostic entry.
    /// </summary>
    /// <param name="entry">The entry to record.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RecordAsync(DiagnosticEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all diagnostic entries for a given session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A list of entries.</returns>
    Task<IReadOnlyList<DiagnosticEntry>> GetEntriesAsync(string sessionId, CancellationToken ct = default);
}
