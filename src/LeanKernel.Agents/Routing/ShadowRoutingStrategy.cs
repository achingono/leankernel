using System.Diagnostics;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Strategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Routing;

/// <summary>
/// Decorates the authoritative strategy with a parallel non-authoritative shadow invocation.
/// </summary>
public sealed class ShadowRoutingStrategy(
    IAgentStrategy inner,
    AgentFactory agentFactory,
    ShadowComparer comparer,
    IOptions<LeanKernelConfig> config,
    ILogger<ShadowRoutingStrategy> logger,
    IDiagnosticsSink? diagnosticsSink = null) : IAgentStrategy
{
    private readonly IAgentStrategy _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly AgentFactory _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
    private readonly ShadowComparer _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
    private readonly RoutingConfig _routing = config?.Value.Routing ?? throw new ArgumentNullException(nameof(config));
    private readonly ILogger<ShadowRoutingStrategy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IDiagnosticsSink? _diagnosticsSink = diagnosticsSink;

    /// <summary>
    /// Gets the strategy name.
    /// </summary>
    public string Name => _inner.Name;

    /// <summary>
    /// Invokes the primary strategy and configured shadow model in parallel.
    /// </summary>
    /// <param name="context">The strategy context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The authoritative primary response.</returns>
    public async Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var shadowModel = _routing.ShadowModel.Trim();
        if (!_routing.ShadowRoutingEnabled || string.IsNullOrWhiteSpace(shadowModel))
        {
            return await _inner.InvokeAsync(context, ct).ConfigureAwait(false);
        }

        var shadowMessages = AgentInvocationBuilder.BuildMessages(context);
        var shadowOptions = AgentInvocationBuilder.BuildOptions(context);
        var primaryTask = InvokePrimaryAsync(context, ct);
        var shadowTask = InvokeShadowAsync(context, shadowModel, shadowMessages, shadowOptions, ct);

        await Task.WhenAll(primaryTask, shadowTask).ConfigureAwait(false);

        var primary = await primaryTask.ConfigureAwait(false);
        var shadow = await shadowTask.ConfigureAwait(false);
        var comparison = BuildComparison(primary.Response, shadow.Response, shadow.FailureNote);
        var result = new ShadowRoutingResult
        {
            PrimaryModel = primary.Model,
            ShadowModel = shadow.Model,
            PrimaryResponse = primary.Response,
            ShadowResponse = shadow.Response,
            PrimaryLatency = primary.Latency,
            ShadowLatency = shadow.Latency,
            PrimaryTokensUsed = primary.TokensUsed,
            ShadowTokensUsed = shadow.TokensUsed,
            Comparison = comparison,
        };

        await TryPersistDiagnosticsAsync(context, result, ct).ConfigureAwait(false);
        return primary.Response;
    }

    private async Task<PrimaryInvocationOutcome> InvokePrimaryAsync(AgentStrategyContext context, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await _inner.InvokeAsync(context, ct).ConfigureAwait(false);
        stopwatch.Stop();

        return new PrimaryInvocationOutcome(
            response,
            stopwatch.Elapsed,
            context.ModelUsed ?? _agentFactory.DefaultModel,
            context.TokensUsed ?? 0);
    }

    private async Task<ShadowInvocationOutcome> InvokeShadowAsync(
        AgentStrategyContext context,
        string shadowModel,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var chatClient = _agentFactory.GetChatClientForModel(shadowModel);
            var response = await chatClient.GetResponseAsync(messages, options, ct).ConfigureAwait(false);
            stopwatch.Stop();

            return new ShadowInvocationOutcome(
                shadowModel,
                response.Text ?? string.Empty,
                stopwatch.Elapsed,
                ChatResponseMetadataReader.GetTokensUsed(response),
                null);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogDebug(
                ex,
                "Shadow invocation canceled for session {SessionId} turn {TurnId} using model {ShadowModel}",
                context.SessionId,
                context.TurnId,
                shadowModel);

            return new ShadowInvocationOutcome(
                shadowModel,
                string.Empty,
                stopwatch.Elapsed,
                0,
                "canceled");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Shadow invocation failed for session {SessionId} turn {TurnId} using model {ShadowModel}",
                context.SessionId,
                context.TurnId,
                shadowModel);

            return new ShadowInvocationOutcome(
                shadowModel,
                string.Empty,
                stopwatch.Elapsed,
                0,
                ex.Message);
        }
    }

    private ShadowComparison BuildComparison(string primaryResponse, string shadowResponse, string? shadowFailure)
    {
        var comparison = _comparer.Compare(primaryResponse, shadowResponse);
        if (string.IsNullOrWhiteSpace(shadowFailure))
        {
            return comparison;
        }

        var note = string.IsNullOrWhiteSpace(comparison.Notes)
            ? $"shadow invocation failed: {shadowFailure}"
            : $"{comparison.Notes}; shadow invocation failed: {shadowFailure}";

        return comparison with { Notes = note };
    }

    private async Task TryPersistDiagnosticsAsync(AgentStrategyContext context, ShadowRoutingResult result, CancellationToken ct)
    {
        if (_diagnosticsSink is null)
        {
            return;
        }

        try
        {
            await _diagnosticsSink.RecordAsync(
                new DiagnosticEntry
                {
                    SessionId = context.SessionId,
                    TurnId = context.TurnId,
                    Category = DiagnosticCategory.Shadow.ToString(),
                    Payload = result,
                },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug(
                ex,
                "Shadow diagnostics persistence canceled for session {SessionId} turn {TurnId}",
                context.SessionId,
                context.TurnId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Shadow diagnostics persistence failed for session {SessionId} turn {TurnId}",
                context.SessionId,
                context.TurnId);
        }
    }

    private sealed record PrimaryInvocationOutcome(string Response, TimeSpan Latency, string Model, int TokensUsed);

    private sealed record ShadowInvocationOutcome(string Model, string Response, TimeSpan Latency, int TokensUsed, string? FailureNote);
}
