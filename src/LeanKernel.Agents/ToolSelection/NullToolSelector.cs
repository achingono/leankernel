using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.ToolSelection;

/// <summary>
/// Provides functionality for null tool selector.
/// </summary>
public sealed class NullToolSelector : IToolSelector
{
    public static readonly NullToolSelector Instance = new();

    private NullToolSelector() { }

    public Task<IReadOnlyList<ToolDefinition>> SelectToolsAsync(
        string userMessage,
        IReadOnlyList<ToolDefinition> allTools,
        int maxTools,
        CancellationToken ct = default)
    {
        return Task.FromResult(allTools);
    }
}
