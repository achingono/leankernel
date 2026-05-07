using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Routing;

/// <summary>
/// Aggregates collaborators used by the model routing service.
/// </summary>
public sealed class ModelRoutingDependencies
{
    public ModelRoutingDependencies(
        TaskComplexityScorer scorer,
        PolicyModelSelector selector,
        ResponseQualityGate qualityGate,
        ProviderHealthTracker healthTracker,
        SpendGuard spendGuard,
        AgentFactory agentFactory)
    {
        Scorer = scorer;
        Selector = selector;
        QualityGate = qualityGate;
        HealthTracker = healthTracker;
        SpendGuard = spendGuard;
        AgentFactory = agentFactory;
    }

    public TaskComplexityScorer Scorer { get; }
    public PolicyModelSelector Selector { get; }
    public ResponseQualityGate QualityGate { get; }
    public ProviderHealthTracker HealthTracker { get; }
    public SpendGuard SpendGuard { get; }
    public AgentFactory AgentFactory { get; }
}

/// <summary>
/// Orchestrates intelligent model selection, invocation, quality checking,
/// and escalation for a single request (FR-1 through FR-8).
/// </summary>
public sealed class ModelRoutingService
{
    private readonly TaskComplexityScorer _scorer;
    private readonly PolicyModelSelector _selector;
    private readonly ResponseQualityGate _qualityGate;
    private readonly ProviderHealthTracker _healthTracker;
    private readonly SpendGuard _spendGuard;
    private readonly AgentFactory _agentFactory;
    private readonly RoutingConfig _config;
    private readonly ILogger<ModelRoutingService> _logger;

    public ModelRoutingService(
        ModelRoutingDependencies dependencies,
        IOptions<LeanKernelConfig> config,
        ILogger<ModelRoutingService> logger)
    {
        _scorer = dependencies.Scorer;
        _selector = dependencies.Selector;
        _qualityGate = dependencies.QualityGate;
        _healthTracker = dependencies.HealthTracker;
        _spendGuard = dependencies.SpendGuard;
        _agentFactory = dependencies.AgentFactory;
        _config = config.Value.Routing;
        _logger = logger;
    }

    /// <summary>
    /// Classify, select, invoke, and quality-check for the given request.
    /// Returns the best response obtained and the selection metadata.
    /// </summary>
    public async Task<(string Response, SelectionResult Metadata)> RouteAsync(
        string requestId,
        string prompt,
        int existingContextTokens,
        string systemInstructions,
        IReadOnlyList<AITool>? tools,
        CancellationToken ct)
    {
        var overallSw = Stopwatch.StartNew();

        // FR-1: Classify complexity.
        var (complexity, estimatedTokens, constraintCount) =
            _scorer.Score(prompt, existingContextTokens);

        _logger.LogInformation(
            "Routing [{RequestId}]: complexity={Complexity}, ~{Tokens} tokens, {Constraints} constraints",
            requestId, complexity, estimatedTokens, constraintCount);

        // Build ordered candidate chain (FR-3).
        var candidates = _selector.BuildCandidates(complexity);
        var fallbackPath = new List<string>();
        var attemptCount = 0;
        string? lastResponse = null;
        string selectionReason = "initial";
        RouteCandidate? selectedCandidate = null;
        bool qualityGateTriggered = false;

        var budgetDeadline = DateTimeOffset.UtcNow.AddMilliseconds(_config.MaxSelectionBudgetMs);

        foreach (var candidate in candidates)
        {
            if (ShouldStopRouting(attemptCount, budgetDeadline, requestId))
                break;

            if (_healthTracker.IsOnCooldown(candidate.Alias))
            {
                _logger.LogDebug(
                    "Routing [{RequestId}]: skipping {Alias} (on cooldown)",
                    requestId, candidate.Alias);
                fallbackPath.Add($"{candidate.Alias}:skipped(cooldown)");
                continue;
            }

            attemptCount++;
            fallbackPath.Add(candidate.Alias);

            LogCandidateAttempt(requestId, attemptCount, candidate);
            var attempt = await InvokeCandidateSafelyAsync(candidate, requestId, systemInstructions, tools, prompt, ct);
            if (attempt.SelectionReason is not null)
            {
                selectionReason = attempt.SelectionReason;
                continue;
            }

            lastResponse = attempt.Response ?? string.Empty;

            // FR-4: Quality gate — only when EnableQualityEscalation=true (Phase 3+).
            if (ShouldEscalateForQuality(lastResponse, prompt, constraintCount, candidate, requestId, out var failReason))
            {
                qualityGateTriggered = true;
                selectionReason = $"escalation:quality_gate({failReason})";
                continue;
            }

            // Response accepted.
            selectedCandidate = candidate;
            selectionReason = attemptCount == 1 ? "free_first" : selectionReason;
            break;
        }

        overallSw.Stop();

        // Use the last response if we exhausted candidates without a passing result.
        var finalResponse = lastResponse ?? string.Empty;
        selectedCandidate ??= candidates.FirstOrDefault() ??
            new RouteCandidate { Tier = "unknown", Alias = _config.SmallAlias };

        // FR-8: Record paid request if applicable.
        if (selectedCandidate.IsPaid)
            _spendGuard.RecordPaidRequest();

        var metadata = new SelectionResult
        {
            RequestId = requestId,
            Complexity = complexity,
            SelectedAlias = selectedCandidate.Alias,
            SelectedTier = selectedCandidate.Tier,
            SelectionReason = selectionReason,
            CostBucket = selectedCandidate.IsPaid ? "paid" : "free",
            AttemptCount = attemptCount,
            FallbackPath = fallbackPath,
            LatencyMs = overallSw.ElapsedMilliseconds,
            EstimatedInputTokens = estimatedTokens,
            ConstraintCount = constraintCount,
            QualityGateTriggered = qualityGateTriggered
        };

        // FR-7: Structured selection log.
        EmitSelectionLog(metadata);

        return (finalResponse, metadata);
    }

    private bool ShouldStopRouting(int attemptCount, DateTimeOffset budgetDeadline, string requestId)
    {
        if (attemptCount >= _config.MaxProviderAttempts)
        {
            _logger.LogWarning(
                "Routing [{RequestId}]: max attempts ({Max}) reached",
                requestId, _config.MaxProviderAttempts);
            return true;
        }

        if (DateTimeOffset.UtcNow < budgetDeadline)
            return false;

        _logger.LogWarning(
            "Routing [{RequestId}]: selection time budget exhausted",
            requestId);
        return true;
    }

    private void LogCandidateAttempt(string requestId, int attemptCount, RouteCandidate candidate)
    {
        _logger.LogDebug(
            "Routing [{RequestId}]: attempt {Attempt} via alias '{Alias}' (tier={Tier}, paid={Paid})",
            requestId, attemptCount, candidate.Alias, candidate.Tier, candidate.IsPaid);
    }

    private async Task<CandidateAttemptResult> InvokeCandidateSafelyAsync(
        RouteCandidate candidate,
        string requestId,
        string systemInstructions,
        IReadOnlyList<AITool>? tools,
        string prompt,
        CancellationToken ct)
    {
        try
        {
            var response = await InvokeAsync(candidate.Alias, systemInstructions, tools, prompt, ct);
            return new CandidateAttemptResult(response, null);
        }
        catch (Exception ex) when (IsTransientFailure(ex, out var statusCode))
        {
            _logger.LogWarning(
                "Routing [{RequestId}]: transient failure on '{Alias}' (status={Status}), marking cooldown",
                requestId, candidate.Alias, statusCode);

            _healthTracker.MarkCooledDown(candidate.Alias);
            return new CandidateAttemptResult(null, $"fallback:transient_{statusCode}");
        }
    }

    private bool ShouldEscalateForQuality(
        string response,
        string prompt,
        int constraintCount,
        RouteCandidate candidate,
        string requestId,
        out string failReason)
    {
        failReason = string.Empty;
        if (!_config.EnableQualityEscalation)
            return false;

        if (_qualityGate.Passes(response, prompt, constraintCount, out var reason))
        {
            return false;
        }

        failReason = reason ?? "quality_gate_failed";
        _logger.LogInformation(
            "Routing [{RequestId}]: quality gate failed on '{Alias}' ({Reason}), escalating",
            requestId, candidate.Alias, failReason);
        return true;
    }

    private async Task<string> InvokeAsync(
        string alias,
        string systemInstructions,
        IReadOnlyList<AITool>? tools,
        string userPrompt,
        CancellationToken ct)
    {
        var agent = _agentFactory.CreateAgentForModel(alias, systemInstructions, tools);
        var messages = new[] { new ChatMessage(ChatRole.User, userPrompt) };
        var session = await agent.CreateSessionAsync(ct);
        var result = await agent.RunAsync(messages, session, cancellationToken: ct);
        return result.Text ?? string.Empty;
    }

    /// <summary>
    /// Emits a structured JSON selection log entry (FR-7, AC-8).
    /// </summary>
    private void EmitSelectionLog(SelectionResult r)
    {
        _logger.LogInformation(
            "SelectionLog: {RequestId} complexity={Complexity} alias={Alias} tier={Tier} " +
            "cost_bucket={CostBucket} reason={Reason} attempts={Attempts} " +
            "fallback_path={FallbackPath} latency_ms={Latency} " +
            "input_tokens={InputTokens} constraints={Constraints} quality_gate={QualityGate}",
            r.RequestId, r.Complexity, r.SelectedAlias, r.SelectedTier,
            r.CostBucket, r.SelectionReason, r.AttemptCount,
            string.Join("->", r.FallbackPath), r.LatencyMs,
            r.EstimatedInputTokens, r.ConstraintCount, r.QualityGateTriggered);
    }

    private static bool IsTransientFailure(Exception ex, out int statusCode)
    {
        statusCode = 0;

        // Check for HttpRequestException with status codes 429, 500, 502, 503, 504.
        if (ex is HttpRequestException httpEx && httpEx.StatusCode is { } code)
        {
            var codeInt = (int)code;
            if (codeInt == 429 || codeInt >= 500)
            {
                statusCode = codeInt;
                return true;
            }
        }

        // Check inner exception for nested HttpRequestException.
        if (ex.InnerException is HttpRequestException innerHttp && innerHttp.StatusCode is { } innerCode)
        {
            var codeInt = (int)innerCode;
            if (codeInt == 429 || codeInt >= 500)
            {
                statusCode = codeInt;
                return true;
            }
        }

        // Match common OpenAI SDK exception messages containing status codes.
        var msg = ex.Message;
        int[] transientCodes = [429, 500, 502, 503, 504];
        foreach (var c in transientCodes)
        {
            if (msg.Contains(c.ToString()))
            {
                statusCode = c;
                return true;
            }
        }

        return false;
    }

    private sealed record CandidateAttemptResult(string? Response, string? SelectionReason);
}
