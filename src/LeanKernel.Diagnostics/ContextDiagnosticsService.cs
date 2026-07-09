using System.Text.Json;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Diagnostics;

/// <summary>
/// Provides functionality for context diagnostics service.
/// </summary>
public sealed class ContextDiagnosticsService(
    ILogger<ContextDiagnosticsService> logger,
    IOptions<DiagnosticsConfig> diagnosticsConfig,
    IOptions<LeanKernelConfig> leanKernelConfig,
    IDiagnosticsSink? sink = null) : IContextDiagnosticsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<ContextDiagnosticsService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly DiagnosticsConfig _diagnosticsConfig = diagnosticsConfig?.Value ?? throw new ArgumentNullException(nameof(diagnosticsConfig));
    private readonly LeanKernelConfig _leanKernelConfig = leanKernelConfig?.Value ?? throw new ArgumentNullException(nameof(leanKernelConfig));
    private readonly IDiagnosticsSink? _sink = sink;

    public async Task StoreContextDiagnosticsAsync(
        string sessionId,
        string turnId,
        ContextDiagnosticsSnapshot snapshot,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!IsEnabled() || !_diagnosticsConfig.PersistToDatabase || _sink is null)
        {
            return;
        }

        await _sink.RecordAsync(new DiagnosticEntry
        {
            SessionId = sessionId,
            TurnId = turnId,
            Category = DiagnosticCategory.ContextSnapshot.ToString(),
            Payload = snapshot,
            Timestamp = snapshot.Timestamp,
        }, ct).ConfigureAwait(false);
    }

    public async Task<ContextDiagnosticsResponse?> GetContextDiagnosticsAsync(
        string sessionId,
        string? turnId = null,
        CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotEntryAsync(sessionId, turnId, ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            return null;
        }

        var admissions = snapshot.Snapshot.Admissions;
        var admitted = admissions.Count(static admission => admission.Admitted);

        return new ContextDiagnosticsResponse
        {
            SessionId = snapshot.SessionId,
            TurnId = snapshot.TurnId,
            Timestamp = snapshot.Timestamp,
            Admissions = admissions,
            TotalCandidatesConsidered = admissions.Count,
            TotalAdmitted = admitted,
            TotalExcluded = admissions.Count - admitted,
            RetrievalDiagnostics = snapshot.Snapshot.RetrievalDiagnostics,
        };
    }

    public async Task<BudgetDiagnosticsResponse?> GetBudgetDiagnosticsAsync(
        string sessionId,
        string? turnId = null,
        CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotEntryAsync(sessionId, turnId, ct).ConfigureAwait(false);
        if (snapshot?.Snapshot.BudgetUsage is null)
        {
            return null;
        }

        return new BudgetDiagnosticsResponse
        {
            SessionId = snapshot.SessionId,
            TurnId = snapshot.TurnId,
            TotalBudgetTokens = ResolveTotalBudgetTokens(snapshot.Snapshot),
            UsableBudgetTokens = snapshot.Snapshot.Budget.TotalTokens,
            ResponseHeadroomRatio = ResolveResponseHeadroomRatio(snapshot.Snapshot),
            Usage = snapshot.Snapshot.BudgetUsage,
            SystemPrompt = CreateDetail(snapshot.Snapshot.Budget.SystemPromptBudget, snapshot.Snapshot.BudgetUsage.SystemPromptUsed),
            WikiFacts = CreateDetail(snapshot.Snapshot.Budget.WikiFactsBudget, snapshot.Snapshot.BudgetUsage.WikiFactsUsed),
            Retrieval = CreateDetail(snapshot.Snapshot.Budget.RetrievalBudget, snapshot.Snapshot.BudgetUsage.RetrievalUsed),
            Conversation = CreateDetail(snapshot.Snapshot.Budget.ConversationBudget, snapshot.Snapshot.BudgetUsage.ConversationUsed),
            Tools = CreateDetail(snapshot.Snapshot.Budget.ToolsBudget, snapshot.Snapshot.BudgetUsage.ToolsUsed),
        };
    }

    public async Task<HistoryDiagnosticsResponse?> GetHistoryDiagnosticsAsync(
        string sessionId,
        string? turnId = null,
        CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotEntryAsync(sessionId, turnId, ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            return null;
        }

        var shaping = snapshot.Snapshot.HistoryDiagnostics;
        return new HistoryDiagnosticsResponse
        {
            SessionId = snapshot.SessionId,
            TurnId = snapshot.TurnId,
            Shaping = shaping,
            VerbatimTurns = shaping?.VerbatimTurns ?? 0,
            CompactedTurns = shaping?.CompactedTurns ?? 0,
            SummarizedTurns = shaping?.SummarizedTurns ?? 0,
            DroppedTurns = shaping?.DroppedTurns ?? 0,
            TokensSaved = shaping is null ? 0 : Math.Max(0, shaping.TotalTokensBefore - shaping.TotalTokensAfter),
        };
    }

    private async Task<StoredSnapshot?> GetSnapshotEntryAsync(
        string sessionId,
        string? turnId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!IsEnabled() || _sink is null)
        {
            return null;
        }

        IReadOnlyList<DiagnosticEntry> entries;
        if (_sink is IDiagnosticsQuerySink querySink)
        {
            entries = await querySink.GetEntriesAsync(
                sessionId,
                DiagnosticCategory.ContextSnapshot.ToString(),
                turnId,
                _diagnosticsConfig.MaxDiagnosticsPerSession > 0 ? _diagnosticsConfig.MaxDiagnosticsPerSession : null,
                ct).ConfigureAwait(false);
        }
        else
        {
            entries = await _sink.GetEntriesAsync(sessionId, ct).ConfigureAwait(false);
        }

        if (entries.Count == 0)
        {
            return null;
        }

        var snapshotEntries = entries
            .Where(entry => string.Equals(entry.Category, DiagnosticCategory.ContextSnapshot.ToString(), StringComparison.Ordinal))
            .OrderBy(entry => entry.Timestamp)
            .ToList();

        var maxDiagnostics = _diagnosticsConfig.MaxDiagnosticsPerSession;
        if (maxDiagnostics > 0 && snapshotEntries.Count > maxDiagnostics)
        {
            snapshotEntries = snapshotEntries.Skip(snapshotEntries.Count - maxDiagnostics).ToList();
        }

        var parsedSnapshots = new List<StoredSnapshot>(snapshotEntries.Count);
        foreach (var entry in snapshotEntries)
        {
            var snapshot = TryDeserialize(entry);
            if (snapshot is null)
            {
                continue;
            }

            parsedSnapshots.Add(snapshot);
        }

        if (parsedSnapshots.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(turnId))
        {
            return parsedSnapshots.LastOrDefault(snapshot => string.Equals(snapshot.TurnId, turnId, StringComparison.Ordinal));
        }

        return parsedSnapshots[^1];
    }

    private StoredSnapshot? TryDeserialize(DiagnosticEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.TurnId))
        {
            _logger.LogWarning(
                "Skipping context snapshot diagnostic {DiagnosticEntryId} for session {SessionId} because turn id is missing",
                entry.Id,
                entry.SessionId);
            return null;
        }

        try
        {
            var snapshot = entry.Payload switch
            {
                ContextDiagnosticsSnapshot typedSnapshot => typedSnapshot,
                JsonElement jsonElement => jsonElement.Deserialize<ContextDiagnosticsSnapshot>(SerializerOptions),
                string json => JsonSerializer.Deserialize<ContextDiagnosticsSnapshot>(json, SerializerOptions),
                _ => JsonSerializer.Deserialize<ContextDiagnosticsSnapshot>(JsonSerializer.Serialize(entry.Payload, SerializerOptions), SerializerOptions),
            };

            if (snapshot is null)
            {
                _logger.LogWarning(
                    "Skipping context snapshot diagnostic {DiagnosticEntryId} for session {SessionId} because payload could not be deserialized",
                    entry.Id,
                    entry.SessionId);
                return null;
            }

            return new StoredSnapshot(entry.SessionId, entry.TurnId, entry.Timestamp, snapshot);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Skipping malformed context snapshot diagnostic {DiagnosticEntryId} for session {SessionId}",
                entry.Id,
                entry.SessionId);
            return null;
        }
    }

    private bool IsEnabled()
        => _diagnosticsConfig.Enabled && _diagnosticsConfig.ContextDiagnosticsEnabled;

    private int ResolveTotalBudgetTokens(ContextDiagnosticsSnapshot snapshot)
    {
        if (snapshot.TotalBudgetTokens > 0)
        {
            return snapshot.TotalBudgetTokens;
        }

        var headroomRatio = ResolveResponseHeadroomRatio(snapshot);
        if (headroomRatio >= 1)
        {
            return snapshot.Budget.TotalTokens;
        }

        return (int)Math.Round(snapshot.Budget.TotalTokens / (1 - headroomRatio), MidpointRounding.AwayFromZero);
    }

    private double ResolveResponseHeadroomRatio(ContextDiagnosticsSnapshot snapshot)
        => snapshot.ResponseHeadroomRatio > 0
            ? snapshot.ResponseHeadroomRatio
            : _leanKernelConfig.Context.ResponseHeadroomRatio;

    private static BudgetCategoryDetail CreateDetail(int allocated, int used)
        => new()
        {
            Allocated = allocated,
            Used = used,
        };

    private sealed record StoredSnapshot(
        string SessionId,
        string TurnId,
        DateTimeOffset Timestamp,
        ContextDiagnosticsSnapshot Snapshot);
}
