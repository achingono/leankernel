namespace LeanKernel.Logic.Tools.Dynamic;

/// <summary>
/// Represents a parsed SKILL.md manifest defining one or more HTTP tool operations.
/// </summary>
public sealed class SkillDefinition
{
    /// <summary>
    /// Gets the skill identifier used as the tool-name prefix.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the skill description surfaced in each derived tool description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional category metadata for governance.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Gets the runtime configuration.
    /// </summary>
    public SkillRuntimeConfig Runtime { get; init; } = new();

    /// <summary>
    /// Gets the egress allowlist for this skill.
    /// </summary>
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];

    /// <summary>
    /// Gets the declared operations.
    /// </summary>
    public IReadOnlyList<SkillOperation> Operations { get; init; } = [];
}