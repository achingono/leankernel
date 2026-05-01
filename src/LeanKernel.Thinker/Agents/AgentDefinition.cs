namespace LeanKernel.Thinker.Agents;

/// <summary>
/// Defines an agent's capabilities and constraints.
/// Used by the orchestrator to decide which workers to delegate to.
/// </summary>
public sealed record AgentDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string SystemPrompt { get; init; }

    /// <summary>Maximum token budget this agent can use for context.</summary>
    public int MaxContextTokens { get; init; } = 4_000;

    /// <summary>Tools this agent can access.</summary>
    public List<string> AllowedTools { get; init; } = [];

    /// <summary>Categories of tasks this agent handles.</summary>
    public List<string> Categories { get; init; } = [];

    /// <summary>
    /// Knowledge tags this agent can access for document/wiki search.
    /// Use ["*"] for unrestricted access. Empty means wiki-only (default).
    /// </summary>
    public List<string> KnowledgeTags { get; init; } = ["wiki"];
}
