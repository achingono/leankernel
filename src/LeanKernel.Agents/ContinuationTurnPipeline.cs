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
    private readonly ContinuationConfig _config;
    private readonly ILogger<ContinuationTurnPipeline> _logger;
    private readonly LeanKernelMetrics? _metrics;
    private readonly TimeProvider _timeProvider;

    public sealed record ContinuationPipelineOptions(
        TurnPipeline Inner,
        TaskCompletionEvaluator Evaluator,
        ISessionTurnCoordinator SessionTurnCoordinator,
        ISessionStore SessionStore,
        IOptions<LeanKernelConfig> Config,
        ILogger<ContinuationTurnPipeline> Logger,
        ITurnProgressBroker? ProgressBroker = null,
        LeanKernelMetrics? Metrics = null,
        TimeProvider? TimeProvider = null);

    private sealed class ContinuationRoundState
    {
        public AgentResponse Response { get; set; } = default!;
        public LeanKernelMessage InitialMessage { get; set; } = default!;
        public string SessionId { get; set; } = string.Empty;
        public string RootTurnId { get; set; } = string.Empty;
        public int Rounds { get; set; }
        public string PreviousNormalized { get; set; } = string.Empty;
        public DateTimeOffset StartedAt { get; set; }
        public ITurnLease Lease { get; set; } = default!;
        public CancellationToken CancellationToken { get; set; }
    }

    public ContinuationTurnPipeline(ContinuationPipelineOptions options)
    {
        _inner = options.Inner ?? throw new ArgumentNullException(nameof(options.Inner));
        _evaluator = options.Evaluator ?? throw new ArgumentNullException(nameof(options.Evaluator));
        _sessionTurnCoordinator = options.SessionTurnCoordinator ?? throw new ArgumentNullException(nameof(options.SessionTurnCoordinator));
        _sessionStore = options.SessionStore ?? throw new ArgumentNullException(nameof(options.SessionStore));
        _config = (options.Config ?? throw new ArgumentNullException(nameof(options.Config))).Value.Continuation;
        _logger = options.Logger ?? throw new ArgumentNullException(nameof(options.Logger));
        _progressBroker = options.ProgressBroker;
        _metrics = options.Metrics;
        _timeProvider = options.TimeProvider ?? TimeProvider.System;
    }

    public ContinuationTurnPipeline(
        TurnPipeline inner,
        TaskCompletionEvaluator evaluator,
        ISessionTurnCoordinator sessionTurnCoordinator,
        ISessionStore sessionStore,
        IOptions<LeanKernelConfig> config,
        ILogger<ContinuationTurnPipeline> logger,
        ITurnProgressBroker? progressBroker = null,
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

        response = await RunContinuationLoopAsync(new ContinuationRoundState
        {
            Response = response,
            InitialMessage = initialMessage,
            SessionId = sessionId,
            RootTurnId = rootTurnId,
            Rounds = rounds,
            PreviousNormalized = previousNormalized,
            StartedAt = startedAt,
            Lease = lease,
            CancellationToken = ct,
        }).ConfigureAwait(false);

        return response;
    }

    private async Task<AgentResponse> RunContinuationLoopAsync(ContinuationRoundState state)
    {
        while (!state.CancellationToken.IsCancellationRequested)
        {
            AgentResponse? terminalResponse;
            (terminalResponse, state.Response, state.Rounds, state.PreviousNormalized) = await TryContinuationRoundAsync(state).ConfigureAwait(false);

            if (terminalResponse is not null)
            {
                return terminalResponse;
            }
        }

        _metrics?.RecordContinuationTermination("cancelled");
        return state.Response;
    }

    private async Task<(AgentResponse? Result, AgentResponse UpdatedResponse, int Rounds, string PreviousNormalized)> TryContinuationRoundAsync(ContinuationRoundState state)
    {
        var elapsed = _timeProvider.GetUtcNow() - state.StartedAt;

        if (state.Rounds >= Math.Max(0, _config.MaxAutoContinuations))
        {
            _metrics?.RecordContinuationTermination("max_rounds");
            return (AppendPauseNote(state.Response, "I've paused here after several continuation rounds. Say 'continue' to resume."), state.Response, state.Rounds, state.PreviousNormalized);
        }

        if (elapsed > TimeSpan.FromSeconds(Math.Max(0, _config.MaxTotalDurationSeconds)))
        {
            _metrics?.RecordContinuationTermination("max_duration");
            return (AppendPauseNote(state.Response, "I've paused here due to turn time limits. Say 'continue' to resume."), state.Response, state.Rounds, state.PreviousNormalized);
        }

        if (state.Lease.PreemptionRequested)
        {
            _metrics?.RecordContinuationTermination("preempted");
            return (AppendPauseNote(state.Response, "I've paused here because a newer message arrived in this chat."), state.Response, state.Rounds, state.PreviousNormalized);
        }

        if (state.Response.Execution?.ToolInvocationCount <= 0)
        {
            _metrics?.RecordContinuationTermination("zero_tool_round");
            return (AppendPauseNote(state.Response, "The tool returned no data — can't proceed."), state.Response, state.Rounds, state.PreviousNormalized);
        }

        var assessment = _evaluator.Assess(state.InitialMessage.Content, state.Response);
        if (assessment.IsComplete)
        {
            _metrics?.RecordContinuationTermination("complete");
            return (state.Response, state.Response, state.Rounds, state.PreviousNormalized);
        }

        var normalized = NormalizeText(state.Response.Content);
        if (!string.IsNullOrEmpty(state.PreviousNormalized) && string.Equals(state.PreviousNormalized, normalized, StringComparison.Ordinal))
        {
            _metrics?.RecordContinuationTermination("no_progress");
            return (AppendPauseNote(state.Response, "I've paused because recent continuation rounds produced the same result."), state.Response, state.Rounds, state.PreviousNormalized);
        }

        state.PreviousNormalized = normalized;
        state.Rounds++;
        _metrics?.RecordContinuationRound();

        var continuationTurnId = Guid.NewGuid().ToString();
        await PublishProgressAsync(
            new TurnProgressUpdate(
                state.SessionId,
                state.RootTurnId,
                TurnProgressKind.ContinuationStarted,
                ToolName: null,
                Message: $"Continuing work (round {state.Rounds})...",
                _timeProvider.GetUtcNow()),
            state.CancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(assessment.ProgressNote))
        {
            await PublishProgressAsync(
                new TurnProgressUpdate(
                    state.SessionId,
                    state.RootTurnId,
                    TurnProgressKind.StatusNote,
                    ToolName: null,
                    Message: assessment.ProgressNote,
                    _timeProvider.GetUtcNow()),
                state.CancellationToken).ConfigureAwait(false);
        }

        var continuationMessage = new LeanKernelMessage
        {
            Content = ContinuePrompt,
            SenderId = state.InitialMessage.SenderId,
            ChannelId = state.InitialMessage.ChannelId,
            SessionId = state.SessionId,
            Timestamp = _timeProvider.GetUtcNow(),
            Metadata = BuildContinuationMetadata(state.InitialMessage.Metadata, state.Rounds, continuationTurnId, state.RootTurnId),
        };

        _logger.LogInformation(
            "Starting continuation round {Round} for session {SessionId}",
            state.Rounds,
            state.SessionId);

        var newResponse = await _inner.ProcessDetailedAsync(continuationMessage, state.CancellationToken).ConfigureAwait(false);
        return (null, newResponse, state.Rounds, state.PreviousNormalized);
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
