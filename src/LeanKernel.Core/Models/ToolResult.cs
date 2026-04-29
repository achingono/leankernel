namespace LeanKernel.Core.Models;

/// <summary>
/// Result of a tool/plugin execution.
/// </summary>
public sealed class ToolResult
{
    public required string ToolName { get; init; }
    public required bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
}
