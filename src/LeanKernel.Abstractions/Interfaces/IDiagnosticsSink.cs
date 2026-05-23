using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface IDiagnosticsSink
{
    Task RecordAsync(DiagnosticEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<DiagnosticEntry>> GetEntriesAsync(string sessionId, CancellationToken ct = default);
}
