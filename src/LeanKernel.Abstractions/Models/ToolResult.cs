namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents the result of a tool execution.
/// </summary>
public sealed record ToolResult
{
    /// <summary>
    /// Gets the name of the tool that was executed.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the tool execution was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the output from the tool execution, if successful.
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Gets the error message from the tool execution, if unsuccessful.
    /// </summary>
    public string? Error { get; init; }
}
