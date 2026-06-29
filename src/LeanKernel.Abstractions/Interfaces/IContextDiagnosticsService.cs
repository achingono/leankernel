using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Defines the contract for icontext diagnostics service.
/// </summary>
public interface IContextDiagnosticsService
{
    Task StoreContextDiagnosticsAsync(string sessionId, string turnId, ContextDiagnosticsSnapshot snapshot, CancellationToken ct = default);
    Task<ContextDiagnosticsResponse?> GetContextDiagnosticsAsync(string sessionId, string? turnId = null, CancellationToken ct = default);
    Task<BudgetDiagnosticsResponse?> GetBudgetDiagnosticsAsync(string sessionId, string? turnId = null, CancellationToken ct = default);
    Task<HistoryDiagnosticsResponse?> GetHistoryDiagnosticsAsync(string sessionId, string? turnId = null, CancellationToken ct = default);
}

/// <summary>
/// Provides functionality for context diagnostics snapshot.
/// </summary>
public sealed record ContextDiagnosticsSnapshot
{
    /// <summary>
    /// Gets or sets admissions.
    /// </summary>
    public IReadOnlyList<ContextAdmissionRecord> Admissions { get; init; } = [];
    /// <summary>
    /// Gets or sets budget usage.
    /// </summary>
    public required ContextBudgetUsage BudgetUsage { get; init; }
    /// <summary>
    /// Gets or sets budget.
    /// </summary>
    public required ContextBudget Budget { get; init; }
    /// <summary>
    /// Gets or sets total budget tokens.
    /// </summary>
    public int TotalBudgetTokens { get; init; }
    /// <summary>
    /// Gets or sets response headroom ratio.
    /// </summary>
    public double ResponseHeadroomRatio { get; init; }
    /// <summary>
    /// Gets or sets history diagnostics.
    /// </summary>
    public HistoryShapingDiagnostics? HistoryDiagnostics { get; init; }
    /// <summary>
    /// Gets or sets retrieval diagnostics.
    /// </summary>
    public RetrievalDiagnostics? RetrievalDiagnostics { get; init; }
    /// <summary>
    /// Gets or sets timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
