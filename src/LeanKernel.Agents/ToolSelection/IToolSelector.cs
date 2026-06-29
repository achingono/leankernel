using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.ToolSelection;

/// <summary>
/// Defines the contract for itool selector.
/// </summary>
public interface IToolSelector
{
    Task<IReadOnlyList<ToolDefinition>> SelectToolsAsync(
        string userMessage,
        IReadOnlyList<ToolDefinition> allTools,
        int maxTools,
        CancellationToken ct = default);
}
