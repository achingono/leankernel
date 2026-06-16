using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.ToolSelection;

internal sealed class NullToolSelector : IToolSelector
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
