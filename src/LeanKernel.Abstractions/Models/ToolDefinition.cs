namespace LeanKernel.Abstractions.Models;

public sealed record ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<ToolParameter>? Parameters { get; init; }
    public Func<IDictionary<string, object?>, CancellationToken, Task<ToolResult>>? Handler { get; init; }
}

public sealed record ToolParameter
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Description { get; init; }
    public bool Required { get; init; } = true;
}
