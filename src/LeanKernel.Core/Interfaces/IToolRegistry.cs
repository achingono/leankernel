namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Compile-time-generated tool registry. Enumerates all ITool
/// implementations discovered via [ToolMetadata] source generation.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Gets all registered tools keyed by tool name.
    /// </summary>
    IReadOnlyDictionary<string, ITool> Tools { get; }
    /// <summary>
    /// Gets a tool by name, or null when no matching tool is registered.
    /// </summary>
    ITool? GetTool(string name);
    /// <summary>
    /// Gets the names of all registered tools.
    /// </summary>
    IEnumerable<string> GetToolNames();
}
