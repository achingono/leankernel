using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// A registry for managing and querying available tools.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Gets the tools that are visible given the provided context.
    /// </summary>
    /// <param name="context">The context for visibility assessment.</param>
    /// <returns>A list of visible tools.</returns>
    IReadOnlyList<ToolDefinition> GetVisibleTools(ToolVisibilityContext context);

    /// <summary>
    /// Gets a tool by its name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>The tool definition, if found; otherwise null.</returns>
    ToolDefinition? GetTool(string name);

    /// <summary>
    /// Adds a set of tools to the registry.
    /// </summary>
    /// <param name="tools">The tools to add.</param>
    void AddTools(IEnumerable<ToolDefinition> tools);
}
