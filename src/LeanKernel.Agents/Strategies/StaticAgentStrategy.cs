using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Agents.Strategies;

/// <summary>
/// Default strategy: single-model invocation using the configured default model.
/// </summary>
public sealed class StaticAgentStrategy : IAgentStrategy
{
    private readonly AgentFactory _agentFactory;
    private readonly ILogger<StaticAgentStrategy> _logger;

    public StaticAgentStrategy(AgentFactory agentFactory, ILogger<StaticAgentStrategy> logger)
    {
        ArgumentNullException.ThrowIfNull(agentFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _agentFactory = agentFactory;
        _logger = logger;
    }

    public string Name => "static";

    public async Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messages = AgentInvocationBuilder.BuildMessages(context);
        var options = AgentInvocationBuilder.BuildOptions(context);

        _logger.LogDebug(
            "Invoking model with {MessageCount} messages, {ToolCount} tools",
            messages.Count,
            context.Tools?.Count ?? 0);

        var response = await _agentFactory.ChatClient.GetResponseAsync(messages, options, ct).ConfigureAwait(false);
        var text = response.Text ?? string.Empty;
        context.ModelUsed = _agentFactory.DefaultModel;
        context.TokensUsed = ChatResponseMetadataReader.GetTokensUsed(response);

        _logger.LogDebug("Model response: {Length} chars", text.Length);

        return text;
    }
}
