using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents;

/// <summary>
/// Decorates turn processing with bounded auto-continuation for incomplete long-running tasks.
/// </summary>
public sealed class ContinuationTurnPipeline : ITurnPipeline
{
    private const string ContinuePrompt = "Continue working on the task. Do not repeat completed steps; pick up where you left off.";
    private const string InternalTurnMetadataKey = "internal_turn";
    private const string InternalReasonMetadataKey = "internal_reason";
    private const string InternalContinuationReason = "auto_continuation_prompt";

    private readonly TurnPipeline _inner;
    private readonly TaskCompletionEvaluator _evaluator;
    private readonly ISessionTurnCoordinator _sessionTurnCoordinator;
    private readonly ISessionStore _sessionStore;
    private readonly ITurnProgressBroker? _progressBroker;
    private readonly ISpendGuardService? _spendGuardService;
    private readonly ContinuationConfig _config;
    private readonly ILogger<ContinuationTurnPipeline> _logger;
    private readonly LeanKernelMetrics? _metrics;
    private readonly TimeProvider _timeProvider;

    public ContinuationTurnPipeline(
        TurnPipeline inner,
        TaskCompletionEvaluator evaluator,
        ISessionTurnCoordinator sessionTurnCoordinator,
        ISessionStore sessionStore,
        IOptions<LeanKernelConfig> config,
        ILogger<ContinuationTurnPipeline> logger,
        ITurnProgressBroker? progressBroker = null,
        ISpendGuardService? spendGuardService = null,
        LeanKernelMetrics? metrics = null,
        TimeProvider? timeProvider = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _sessionTurnCoordinator = sessionTurnCoordinator ?? throw new ArgumentNullException(nameof(sessionTurnCoordinator));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _config = (config ?? throw new ArgumentNullException(nameof(config))).Value.Continuation;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _progressBroker = progressBroker;
        _spendGuardService = spendGuardService;
        _metrics = metrics;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<string> ProcessAsync(LeanKernelMessage message, CancellationToken ct = default)
        => (await ProcessDetailedAsync(message, ct).ConfigureAwait(false)).Content;

    public async Task<AgentResponse> ProcessDetailedAsync(LeanKernelMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sessionId = !string.IsNullOrWhiteSpace(message.SessionId)
            ? message.SessionId!
            : await _sessionStore.GetOrCreateSessionIdAsync(message.ChannelId, message.SenderId, ct).ConfigureAwait(false);

        await using var lease = await _sessionTurnCoordinator.BeginTurnAsync(sessionId, ct).ConfigureAwait(false);
        var initialMessage = EnsureSession(message, sessionId);
        var rootTurnId = ResolveRootTurnId(initialMessage.Metadata);
        initialMessage = EnsureRootTurnMetadata(initialMessage, rootTurnId);

        var startedAt = _timeProvider.GetUtcNow();
        var rounds = 0;
        var previousNormalized = string.Empty;
        var response = await _inner.ProcessDetailedAsync(initialMessage, ct).ConfigureAwait(false);

        if (!_config.Enabled || !_evaluator.ShouldAttemptContinuation(initialMessage.Content, response.Execution))
        {
            return response;
        }

        while (!ct.IsCancellationRequested)
        {
            var elapsed = _timeProvider.GetUtcNow() - startedAt;
            if (rounds >= Math.Max(0, _config.MaxAutoContinuations))
            {
                _metrics?.RecordContinuationTermination("max_rounds");
                return AppendPauseNote(response, "I've paused here after several continuation rounds. Say 'continue' to resume.");
            }

            if (elapsed > TimeSpan.FromSeconds(Math.Max(0, _config.MaxTotalDurationSeconds)))
            {
                _metrics?.RecordContinuationTermination("max_duration");
                return AppendPauseNote(response, "I've paused here due to turn time limits. Say 'continue' to resume.");
            }

            if (lease.PreemptionRequested)
            {
                _metrics?.RecordContinuationTermination("preempted");
                return AppendPauseNote(response, "I've paused here because a newer message arrived in this chat.");
            }

            if (response.Execution?.ToolInvocationCount <= 0)
            {
                _metrics?.RecordContinuationTermination("zero_tool_round");
                return response;
            }

            var assessment = _evaluator.Assess(initialMessage.Content, response);
            if (assessment.IsComplete)
            {
                _metrics?.RecordContinuationTermination("complete");
                return response;
            }

            if (_spendGuardService is not null)
            {
                var spendDecision = _spendGuardService.Evaluate(sessionId, ModelTier.Standard, 128, 128);
                if (spendDecision.Action == SpendGuardAction.Block)
                {
                    _metrics?.RecordContinuationTermination("spend_block");
                    return AppendPauseNote(response, spendDecision.Reason);
                }
            }

            var normalized = NormalizeText(response.Content);
            if (!string.IsNullOrEmpty(previousNormalized) && string.Equals(previousNormalized, normalized, StringComparison.Ordinal))
            {
                _metrics?.RecordContinuationTermination("no_progress");
                return AppendPauseNote(response, "I've paused because recent continuation rounds produced the same result.");
            }

            previousNormalized = normalized;
            rounds++;
            _metrics?.RecordContinuationRound();

            var continuationTurnId = Guid.NewGuid().ToString();
            await PublishProgressAsync(
                new TurnProgressUpdate(
                    sessionId,
                    rootTurnId,
                    TurnProgressKind.ContinuationStarted,
                    ToolName: null,
                    Message: $"Continuing work (round {rounds})...",
                    _timeProvider.GetUtcNow()),
                ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(assessment.ProgressNote))
            {
                await PublishProgressAsync(
                    new TurnProgressUpdate(
                        sessionId,
                        rootTurnId,
                        TurnProgressKind.StatusNote,
                        ToolName: null,
                        Message: assessment.ProgressNote,
                        _timeProvider.GetUtcNow()),
                    ct).ConfigureAwait(false);
            }

            var continuationMessage = new LeanKernelMessage
            {
                Content = ContinuePrompt,
                SenderId = initialMessage.SenderId,
                ChannelId = initialMessage.ChannelId,
                SessionId = sessionId,
                Timestamp = _timeProvider.GetUtcNow(),
                Metadata = BuildContinuationMetadata(initialMessage.Metadata, rounds, continuationTurnId, rootTurnId),
            };

            _logger.LogInformation(
                "Starting continuation round {Round} for session {SessionId}",
                rounds,
                sessionId);

            response = await _inner.ProcessDetailedAsync(continuationMessage, ct).ConfigureAwait(false);
        }

        _metrics?.RecordContinuationTermination("cancelled");
        return response;
    }

    private async Task PublishProgressAsync(TurnProgressUpdate update, CancellationToken ct)
    {
        if (_progressBroker is null)
        {
            return;
        }

        await _progressBroker.PublishAsync(update, ct).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> BuildContinuationMetadata(
        IReadOnlyDictionary<string, string>? existing,
        int round,
        string turnId,
        string rootTurnId)
    {
        var metadata = existing is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        metadata["auto_continuation"] = "true";
        metadata[InternalTurnMetadataKey] = "true";
        metadata[InternalReasonMetadataKey] = InternalContinuationReason;
        metadata["continuation_round"] = round.ToString();
        metadata["root_turn_id"] = rootTurnId;
        metadata["rootTurnId"] = rootTurnId;
        metadata["turn_id"] = turnId;
        metadata["turnId"] = turnId;
        return metadata;
    }

    private static string ResolveRootTurnId(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is not null)
        {
            if (metadata.TryGetValue("root_turn_id", out var rootTurnId) && !string.IsNullOrWhiteSpace(rootTurnId))
            {
                return rootTurnId.Trim();
            }

            if (metadata.TryGetValue("rootTurnId", out rootTurnId) && !string.IsNullOrWhiteSpace(rootTurnId))
            {
                return rootTurnId.Trim();
            }

            if (metadata.TryGetValue("turn_id", out var turnId) && !string.IsNullOrWhiteSpace(turnId))
            {
                return turnId.Trim();
            }

            if (metadata.TryGetValue("turnId", out turnId) && !string.IsNullOrWhiteSpace(turnId))
            {
                return turnId.Trim();
            }
        }

        return Guid.NewGuid().ToString();
    }

    private static AgentResponse AppendPauseNote(AgentResponse response, string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return response;
        }

        var content = string.IsNullOrWhiteSpace(response.Content)
            ? note.Trim()
            : $"{response.Content.TrimEnd()}\n\n{note.Trim()}";

        return response with { Content = content };
    }

    private static LeanKernelMessage EnsureSession(LeanKernelMessage message, string sessionId)
        => message with { SessionId = sessionId };

    private static LeanKernelMessage EnsureRootTurnMetadata(LeanKernelMessage message, string rootTurnId)
    {
        var metadata = message.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(message.Metadata, StringComparer.OrdinalIgnoreCase);

        metadata["root_turn_id"] = rootTurnId;
        metadata["rootTurnId"] = rootTurnId;
        metadata["turn_id"] = rootTurnId;
        metadata["turnId"] = rootTurnId;

        return message with { Metadata = metadata };
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(' ', value
            .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToLowerInvariant();
    }
}
