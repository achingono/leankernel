namespace LeanKernel.Abstractions.Models;

public sealed record ToolVisibilityContext
{
    public string? AgentRole { get; init; }
    public string? UserId { get; init; }
    public IReadOnlyList<string>? AllowedCategories { get; init; }
    public IReadOnlyList<string>? AllowedToolNames { get; init; }
}
