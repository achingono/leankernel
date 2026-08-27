namespace LeanKernel.Logic.Tools.ToolSelection;

/// <summary>
/// Selects the most relevant tool subset when the available tool count exceeds the configured limit.
/// </summary>
public interface IToolSelector
{
    Task<IReadOnlyList<ToolDefinition>> SelectToolsAsync(
        string userMessage,
        IReadOnlyList<ToolDefinition> allTools,
        int maxTools,
        CancellationToken cancellationToken = default);
}
