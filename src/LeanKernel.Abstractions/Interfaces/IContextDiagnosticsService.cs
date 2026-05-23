using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface IContextDiagnosticsService
{
    Task StoreContextDiagnosticsAsync(string sessionId, string turnId, ContextDiagnosticsSnapshot snapshot, CancellationToken ct = default);
    Task<ContextDiagnosticsResponse?> GetContextDiagnosticsAsync(string sessionId, string? turnId = null, CancellationToken ct = default);
    Task<BudgetDiagnosticsResponse?> GetBudgetDiagnosticsAsync(string sessionId, string? turnId = null, CancellationToken ct = default);
    Task<HistoryDiagnosticsResponse?> GetHistoryDiagnosticsAsync(string sessionId, string? turnId = null, CancellationToken ct = default);
}

public sealed record ContextDiagnosticsSnapshot
{
    public IReadOnlyList<ContextAdmissionRecord> Admissions { get; init; } = [];
    public required ContextBudgetUsage BudgetUsage { get; init; }
    public required ContextBudget Budget { get; init; }
    public int TotalBudgetTokens { get; init; }
    public double ResponseHeadroomRatio { get; init; }
    public HistoryShapingDiagnostics? HistoryDiagnostics { get; init; }
    public RetrievalDiagnostics? RetrievalDiagnostics { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
