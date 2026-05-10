using Microsoft.Extensions.Logging;
using LeanKernel.Thinker.Routing;

namespace LeanKernel.Thinker.Strategies;

/// <summary>
/// Returns the static model response while recording what dynamic routing would have selected.
/// </summary>
public sealed class ShadowRoutingStrategy : IAgentStrategy
{
    private readonly StaticAgentStrategy _staticStrategy;
    private readonly ModelRoutingService _routing;
    private readonly SelectionLogStore _selectionLog;
    private readonly ILogger<ShadowRoutingStrategy> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShadowRoutingStrategy" /> class.
    /// </summary>
    /// <param name="staticStrategy">The static strategy used for the user-visible response.</param>
    /// <param name="routing">The model routing service used for shadow selection.</param>
    /// <param name="selectionLog">The selection log used for routing observability.</param>
    /// <param name="logger">The logger used for shadow-routing diagnostics.</param>
    public ShadowRoutingStrategy(
        StaticAgentStrategy staticStrategy,
        ModelRoutingService routing,
        SelectionLogStore selectionLog,
        ILogger<ShadowRoutingStrategy> logger)
    {
        _staticStrategy = staticStrategy;
        _routing = routing;
        _selectionLog = selectionLog;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "shadow-routing";

    /// <inheritdoc />
    public async Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct)
    {
        var response = await _staticStrategy.InvokeAsync(context, ct);
        _ = RunShadowRoutingAsync(context, ct);
        return response;
    }

    private async Task RunShadowRoutingAsync(AgentStrategyContext context, CancellationToken ct)
    {
        try
        {
            var (_, metadata) = await _routing.RouteAsync(
                requestId: context.Message.Id,
                prompt: context.Message.Content,
                existingContextTokens: context.Context.EstimatedTotalTokens,
                systemInstructions: context.Instructions,
                tools: context.Tools,
                ct: ct);

            _selectionLog.Record(metadata);

            _logger.LogInformation(
                "Shadow routing [{RequestId}]: would have selected alias='{Alias}' tier='{Tier}' " +
                "complexity={Complexity} cost={CostBucket} reason='{Reason}' attempts={Attempts} latency={LatencyMs}ms",
                metadata.RequestId, metadata.SelectedAlias, metadata.SelectedTier,
                metadata.Complexity, metadata.CostBucket, metadata.SelectionReason,
                metadata.AttemptCount, metadata.LatencyMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shadow routing [{RequestId}]: suppressed exception", context.Message.Id);
        }
    }
}
