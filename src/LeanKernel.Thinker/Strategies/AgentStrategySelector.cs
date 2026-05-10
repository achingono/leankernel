using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Thinker.Strategies;

/// <summary>
/// Selects the model invocation strategy for the current routing configuration.
/// </summary>
public sealed class AgentStrategySelector
{
    private readonly LeanKernelConfig _config;
    private readonly StaticAgentStrategy _staticStrategy;
    private readonly RoutedAgentStrategy _routedStrategy;
    private readonly ShadowRoutingStrategy _shadowRoutingStrategy;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentStrategySelector" /> class.
    /// </summary>
    /// <param name="config">The LeanKernel configuration that controls routing behavior.</param>
    /// <param name="staticStrategy">The static invocation strategy.</param>
    /// <param name="routedStrategy">The routed invocation strategy.</param>
    /// <param name="shadowRoutingStrategy">The shadow-routing invocation strategy.</param>
    public AgentStrategySelector(
        IOptions<LeanKernelConfig> config,
        StaticAgentStrategy staticStrategy,
        RoutedAgentStrategy routedStrategy,
        ShadowRoutingStrategy shadowRoutingStrategy)
    {
        _config = config.Value;
        _staticStrategy = staticStrategy;
        _routedStrategy = routedStrategy;
        _shadowRoutingStrategy = shadowRoutingStrategy;
    }

    /// <summary>
    /// Selects the strategy to use for the current turn.
    /// </summary>
    /// <returns>The selected invocation strategy.</returns>
    public IAgentStrategy Select()
    {
        if (!_config.Routing.Enabled)
        {
            return _staticStrategy;
        }

        return _config.Routing.ShadowMode
            ? _shadowRoutingStrategy
            : _routedStrategy;
    }
}
