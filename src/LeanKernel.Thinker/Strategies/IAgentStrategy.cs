namespace LeanKernel.Thinker.Strategies;

/// <summary>
/// Executes one model invocation strategy for a LeanKernel turn.
/// </summary>
public interface IAgentStrategy
{
    /// <summary>
    /// Gets the stable strategy name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the strategy for the current turn.
    /// </summary>
    /// <param name="context">The strategy context for the turn.</param>
    /// <param name="ct">A token used to cancel invocation.</param>
    /// <returns>The assistant response text.</returns>
    Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct);
}
