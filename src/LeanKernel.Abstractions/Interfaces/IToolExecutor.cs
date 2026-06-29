using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Executes a tool by its name with the given arguments.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Executes the tool.
    /// </summary>
    /// <param name="toolName">The name of the tool to execute.</param>
    /// <param name="arguments">The arguments to pass to the tool.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous execution, returning the tool result.</returns>
    Task<ToolResult> ExecuteAsync(string toolName, IDictionary<string, object?> arguments, CancellationToken ct = default);
}
