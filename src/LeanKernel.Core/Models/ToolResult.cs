namespace LeanKernel.Core.Models;

/// <summary>
/// Result of a tool/plugin execution.
/// </summary>
public sealed class ToolResult
{
    /// <summary>
    /// Gets or sets the tool name.
    /// </summary>
    public required string ToolName { get; init; }
    /// <summary>
    /// Gets or sets the success.
    /// </summary>
    public required bool Success { get; init; }
    /// <summary>
    /// Gets or sets the output.
    /// </summary>
    public string? Output { get; init; }
    /// <summary>
    /// Gets or sets the error.
    /// </summary>
    public string? Error { get; init; }
    /// <summary>
    /// Gets or sets the duration.
    /// </summary>
    public TimeSpan Duration { get; init; }
}
