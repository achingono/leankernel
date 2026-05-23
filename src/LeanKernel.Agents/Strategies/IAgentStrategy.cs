namespace LeanKernel.Agents.Strategies;

/// <summary>
/// Executes one model invocation strategy for a turn.
/// </summary>
public interface IAgentStrategy
{
    string Name { get; }

    Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct = default);
}
