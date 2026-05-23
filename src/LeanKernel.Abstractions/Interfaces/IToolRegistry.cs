using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface IToolRegistry
{
    IReadOnlyList<ToolDefinition> GetVisibleTools(ToolVisibilityContext context);
    ToolDefinition? GetTool(string name);
}
