namespace LeanKernel.Abstractions.Models;

public sealed record ToolResult
{
    public required string ToolName { get; init; }
    public required bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
}
