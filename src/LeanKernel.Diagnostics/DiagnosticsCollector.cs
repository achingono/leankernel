using System.Diagnostics;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Diagnostics;

/// <summary>
/// Central diagnostics collector. Records structured diagnostic entries
/// and emits OpenTelemetry activities for tracing.
/// </summary>
public sealed class DiagnosticsCollector
{
    private static readonly ActivitySource ActivitySource = new("LeanKernel.Diagnostics");

    private readonly IDiagnosticsSink? _sink;
    private readonly DiagnosticsConfig _config;
    private readonly ILogger<DiagnosticsCollector> _logger;

    public DiagnosticsCollector(
        ILogger<DiagnosticsCollector> logger,
        IOptions<DiagnosticsConfig> config,
        IDiagnosticsSink? sink = null)
    {
        _logger = logger;
        _config = config.Value;
        _sink = sink;
    }

    public async Task RecordContextAdmissionAsync(
        string sessionId,
        string turnId,
        IReadOnlyList<ContextAdmissionRecord> admissions,
        CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            return;
        }

        var admittedCount = admissions.Count(static admission => admission.Admitted);
        var excludedCount = admissions.Count - admittedCount;

        using var activity = ActivitySource.StartActivity("ContextAdmission");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("turn.id", turnId);
        activity?.SetTag("admitted.count", admittedCount);
        activity?.SetTag("excluded.count", excludedCount);

        _logger.LogDebug(
            "Context admission: {Admitted} admitted, {Excluded} excluded for session {SessionId}",
            admittedCount,
            excludedCount,
            sessionId);

        if (_config.PersistToDatabase && _sink is not null)
        {
            var entry = new DiagnosticEntry
            {
                SessionId = sessionId,
                TurnId = turnId,
                Category = DiagnosticCategory.ContextAdmission.ToString(),
                Payload = admissions
            };

            await _sink.RecordAsync(entry, ct).ConfigureAwait(false);
        }
    }

    public async Task RecordBudgetUsageAsync(
        string sessionId,
        string turnId,
        ContextBudgetUsage usage,
        CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            return;
        }

        using var activity = ActivitySource.StartActivity("BudgetUsage");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("turn.id", turnId);
        activity?.SetTag("budget.total_used", usage.TotalUsed);
        activity?.SetTag("budget.system_prompt", usage.SystemPromptUsed);
        activity?.SetTag("budget.wiki_facts", usage.WikiFactsUsed);
        activity?.SetTag("budget.conversation", usage.ConversationUsed);
        activity?.SetTag("budget.retrieval", usage.RetrievalUsed);
        activity?.SetTag("budget.tools", usage.ToolsUsed);

        _logger.LogDebug(
            "Budget usage: {TotalUsed} tokens for session {SessionId}",
            usage.TotalUsed,
            sessionId);

        if (_config.PersistToDatabase && _sink is not null)
        {
            var entry = new DiagnosticEntry
            {
                SessionId = sessionId,
                TurnId = turnId,
                Category = DiagnosticCategory.BudgetAllocation.ToString(),
                Payload = usage
            };

            await _sink.RecordAsync(entry, ct).ConfigureAwait(false);
        }
    }

    public async Task RecordToolVisibilityAsync(
        string sessionId,
        string turnId,
        IReadOnlyList<string> visibleTools,
        IReadOnlyList<string> excludedTools,
        CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            return;
        }

        using var activity = ActivitySource.StartActivity("ToolVisibility");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("turn.id", turnId);
        activity?.SetTag("tools.visible", visibleTools.Count);
        activity?.SetTag("tools.excluded", excludedTools.Count);

        _logger.LogDebug(
            "Tool visibility: {Visible} visible, {Excluded} excluded for session {SessionId}",
            visibleTools.Count,
            excludedTools.Count,
            sessionId);

        if (_config.PersistToDatabase && _sink is not null)
        {
            var entry = new DiagnosticEntry
            {
                SessionId = sessionId,
                TurnId = turnId,
                Category = DiagnosticCategory.ToolVisibility.ToString(),
                Payload = new { visible = visibleTools, excluded = excludedTools }
            };

            await _sink.RecordAsync(entry, ct).ConfigureAwait(false);
        }
    }

    public async Task RecordModelRoutingAsync(
        string sessionId,
        string turnId,
        RoutingDecision decision,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        ArgumentNullException.ThrowIfNull(decision);

        if (!_config.Enabled)
        {
            return;
        }

        using var activity = ActivitySource.StartActivity("ModelRouting");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("turn.id", turnId);
        activity?.SetTag("model.selected", decision.SelectedModel);
        activity?.SetTag("model.reason", decision.Reason);
        activity?.SetTag("model.tier", decision.SelectedTier.ToString());
        activity?.SetTag("model.complexity_score", decision.ComplexityScore);
        activity?.SetTag("model.escalation_attempt", decision.EscalationAttempt);

        _logger.LogInformation(
            "Model routing: {Model} ({Reason}) for session {SessionId} on tier {Tier} attempt {Attempt}",
            decision.SelectedModel,
            decision.Reason,
            sessionId,
            decision.SelectedTier,
            decision.EscalationAttempt);

        if (_config.PersistToDatabase && _sink is not null)
        {
            var entry = new DiagnosticEntry
            {
                SessionId = sessionId,
                TurnId = turnId,
                Category = DiagnosticCategory.ModelRouting.ToString(),
                Payload = decision
            };

            await _sink.RecordAsync(entry, ct).ConfigureAwait(false);
        }
    }

    public async Task RecordOrchestrationAsync(
        string sessionId,
        string turnId,
        OrchestrationResult result,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        ArgumentNullException.ThrowIfNull(result);

        if (!_config.Enabled)
        {
            return;
        }

        using var activity = ActivitySource.StartActivity("Orchestration");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("turn.id", turnId);
        activity?.SetTag("orchestration.worker_count", result.TotalWorkerInvocations);
        activity?.SetTag("orchestration.duration_ms", result.TotalDuration.TotalMilliseconds);

        _logger.LogInformation(
            "Orchestration completed for session {SessionId}: workers={WorkerCount}, durationMs={DurationMs:0}",
            sessionId,
            result.TotalWorkerInvocations,
            result.TotalDuration.TotalMilliseconds);

        if (_config.PersistToDatabase && _sink is not null)
        {
            var entry = new DiagnosticEntry
            {
                SessionId = sessionId,
                TurnId = turnId,
                Category = DiagnosticCategory.Orchestration.ToString(),
                Payload = result
            };

            await _sink.RecordAsync(entry, ct).ConfigureAwait(false);
        }
    }

    public async Task RecordQualityGateAsync(
        string sessionId,
        string turnId,
        QualityGateResult result,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        ArgumentNullException.ThrowIfNull(result);

        if (!_config.Enabled)
        {
            return;
        }

        using var activity = ActivitySource.StartActivity("QualityGate");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("turn.id", turnId);
        activity?.SetTag("quality.outcome", result.Outcome.ToString());
        activity?.SetTag("quality.passed", result.Passed);
        activity?.SetTag("quality.score", result.OverallScore);

        if (result.FailureReason is not null)
        {
            activity?.SetTag("quality.fail_reason", result.FailureReason);
        }

        _logger.LogDebug(
            "Quality gate: {Outcome} for session {SessionId} with score {Score:0.00}",
            result.Outcome,
            sessionId,
            result.OverallScore);

        if (_config.PersistToDatabase && _sink is not null)
        {
            var entry = new DiagnosticEntry
            {
                SessionId = sessionId,
                TurnId = turnId,
                Category = DiagnosticCategory.QualityGate.ToString(),
                Payload = result
            };

            await _sink.RecordAsync(entry, ct).ConfigureAwait(false);
        }
    }

    public async Task RecordResponseEnhancementAsync(
        string sessionId,
        string turnId,
        EnhancementResult result,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        ArgumentNullException.ThrowIfNull(result);

        if (!_config.Enabled)
        {
            return;
        }

        using var activity = ActivitySource.StartActivity("ResponseEnhancement");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("turn.id", turnId);
        activity?.SetTag("enhancement.modified", result.WasModified);
        activity?.SetTag("enhancement.step_count", result.Steps.Count);
        activity?.SetTag("enhancement.duration_ms", result.TotalDuration.TotalMilliseconds);

        _logger.LogDebug(
            "Response enhancement: modified={Modified} steps={StepCount} durationMs={DurationMs:0} for session {SessionId}",
            result.WasModified,
            result.Steps.Count,
            result.TotalDuration.TotalMilliseconds,
            sessionId);

        if (_config.PersistToDatabase && _sink is not null)
        {
            var entry = new DiagnosticEntry
            {
                SessionId = sessionId,
                TurnId = turnId,
                Category = DiagnosticCategory.ResponseEnhancement.ToString(),
                Payload = result
            };

            await _sink.RecordAsync(entry, ct).ConfigureAwait(false);
        }
    }

    public Activity? StartTurnActivity(string sessionId, string turnId)
    {
        if (!_config.Enabled)
        {
            return null;
        }

        var activity = ActivitySource.StartActivity("Turn");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("turn.id", turnId);
        return activity;
    }
}
