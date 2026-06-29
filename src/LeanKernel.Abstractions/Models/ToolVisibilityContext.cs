namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Defines the context used to determine tool visibility for a user or agent.
/// </summary>
public sealed record ToolVisibilityContext
{
    /// <summary>
    /// Gets the role of the agent requesting access.
    /// </summary>
    public string? AgentRole { get; init; }

    /// <summary>
    /// Gets the ID of the user.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Gets the list of allowed tool categories.
    /// </summary>
    public IReadOnlyList<string>? AllowedCategories { get; init; }

    /// <summary>
    /// Gets the list of allowed tool names.
    /// </summary>
    public IReadOnlyList<string>? AllowedToolNames { get; init; }
}
