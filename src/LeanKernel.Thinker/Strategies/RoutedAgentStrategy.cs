using LeanKernel.Core.Interfaces;
using LeanKernel.Thinker.Routing;

namespace LeanKernel.Thinker.Strategies;

/// <summary>
/// Invokes the model through the policy-based routing pipeline.
/// </summary>
public sealed class RoutedAgentStrategy : IAgentStrategy
{
    private readonly ModelRoutingService _routing;
    private readonly ISessionStore _sessions;
    private readonly SelectionLogStore _selectionLog;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutedAgentStrategy" /> class.
    /// </summary>
    /// <param name="routing">The model routing service.</param>
    /// <param name="sessions">The session store used to persist routing metadata.</param>
    /// <param name="selectionLog">The selection log used for routing observability.</param>
    public RoutedAgentStrategy(
        ModelRoutingService routing,
        ISessionStore sessions,
        SelectionLogStore selectionLog)
    {
        _routing = routing;
        _sessions = sessions;
        _selectionLog = selectionLog;
    }

    /// <inheritdoc />
    public string Name => "routed";

    /// <inheritdoc />
    public async Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct)
    {
        var (response, metadata) = await _routing.RouteAsync(
            requestId: context.Message.Id,
            prompt: context.Message.Content,
            existingContextTokens: context.Context.EstimatedTotalTokens,
            systemInstructions: context.Instructions,
            tools: context.Tools,
            ct: ct);

        await _sessions.SetMetadataAsync(context.SessionId, "routing:alias", metadata.SelectedAlias, ct);
        await _sessions.SetMetadataAsync(context.SessionId, "routing:tier", metadata.SelectedTier, ct);
        await _sessions.SetMetadataAsync(context.SessionId, "routing:complexity", metadata.Complexity.ToString(), ct);
        await _sessions.SetMetadataAsync(context.SessionId, "routing:cost_bucket", metadata.CostBucket, ct);
        await _sessions.SetMetadataAsync(context.SessionId, "routing:latency_ms", metadata.LatencyMs.ToString(), ct);
        await _sessions.SetMetadataAsync(context.SessionId, "routing:attempts", metadata.AttemptCount.ToString(), ct);

        _selectionLog.Record(metadata);

        return response;
    }
}
