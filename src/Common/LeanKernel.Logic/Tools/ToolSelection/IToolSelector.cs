namespace LeanKernel.Logic.Tools.ToolSelection;

/// <summary>
/// Selects the most relevant tool subset when the available tool count exceeds the configured limit.
/// </summary>
public interface IToolSelector
{
    /// <summary>
    /// Selects the most relevant tools for the given user message.
    /// </summary>
    /// <param name="userMessage">The user message to match against tool descriptions.</param>
    /// <param name="allTools">All available tools.</param>
    /// <param name="maxTools">Maximum number of tools to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The selected tool subset.</returns>
    Task<IReadOnlyList<ToolDefinition>> SelectToolsAsync(
        string userMessage,
        IReadOnlyList<ToolDefinition> allTools,
        int maxTools,
        CancellationToken cancellationToken = default);
}
