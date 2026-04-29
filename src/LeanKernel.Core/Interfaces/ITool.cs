using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Plugin/tool contract. Each tool is discovered at compile time
/// via source-generated registry and exposed to the LLM.
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }

    /// <summary>JSON Schema describing the tool's parameters.</summary>
    string ParametersSchema { get; }

    Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct);
}
