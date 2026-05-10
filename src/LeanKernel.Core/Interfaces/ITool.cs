using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Plugin/tool contract. Each tool is discovered at compile time
/// via source-generated registry and exposed to the LLM.
/// </summary>
public interface ITool
{
    /// <summary>
    /// Gets the stable tool name exposed to the model.
    /// </summary>
    string Name { get; }
    /// <summary>
    /// Gets the human-readable tool description exposed to the model.
    /// </summary>
    string Description { get; }

    /// <summary>Category/domain of this tool (e.g., "scheduling", "search", "code").</summary>
    string Category { get; }

    /// <summary>JSON Schema describing the tool's parameters.</summary>
    string ParametersSchema { get; }

    /// <summary>
    /// Executes the tool with JSON parameters supplied by the model.
    /// </summary>
    Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct);
}
