namespace LeanKernel.Core.Interfaces;

/// <summary>
/// A tool that exposes multiple discrete operations, each with its own
/// schema and description. Tools that implement this interface will have
/// each operation exposed as a separate AI function, giving the LLM
/// precise per-operation schemas instead of a single opaque string input.
/// </summary>
public interface IOperationsTool : ITool
{
    IReadOnlyList<ToolOperationDescriptor> Operations { get; }
}

/// <summary>
/// Describes a single operation within a multi-operation tool.
/// </summary>
/// <param name="Id">Unique operation identifier (used as the function name suffix).</param>
/// <param name="Summary">Human-readable description shown to the LLM.</param>
/// <param name="ParametersSchema">JSON Schema string describing the operation's parameters (excluding the 'operation' field).</param>
public record ToolOperationDescriptor(string Id, string Summary, string ParametersSchema);
