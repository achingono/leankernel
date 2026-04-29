namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Compile-time-generated tool registry. Enumerates all ITool
/// implementations discovered via [ToolMetadata] source generation.
/// </summary>
public interface IToolRegistry
{
    IReadOnlyDictionary<string, ITool> Tools { get; }
    ITool? GetTool(string name);
    IEnumerable<string> GetToolNames();
}
