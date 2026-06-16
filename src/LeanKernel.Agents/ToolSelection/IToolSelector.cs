using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.ToolSelection;

public interface IToolSelector
{
    Task<IReadOnlyList<ToolDefinition>> SelectToolsAsync(
        string userMessage,
        IReadOnlyList<ToolDefinition> allTools,
        int maxTools,
        CancellationToken ct = default);
}
