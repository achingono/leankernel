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

/// <summary>
/// Runtime configuration for an HTTP skill.
/// </summary>
public sealed class SkillRuntimeConfig
{
    /// <summary>
    /// Gets the runtime type. Only "http" is supported in Phase 01.
    /// </summary>
    public string Type { get; init; } = "http";

    /// <summary>
    /// Gets the base URL for HTTP operations.
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets the per-request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets the auth configuration.
    /// </summary>
    public SkillAuthConfig Auth { get; init; } = new();
}

/// <summary>
/// Authentication configuration for a skill.
/// </summary>
public sealed class SkillAuthConfig
{
    /// <summary>
    /// Gets the auth type: "none" or "bearer".
    /// </summary>
    public string Type { get; init; } = "none";

    /// <summary>
    /// Gets the secret reference name resolved from /run/secrets/&lt;ref&gt; or SKILL__&lt;REF&gt;.
    /// </summary>
    public string? SecretRef { get; init; }
}

/// <summary>
/// A single HTTP operation derived from a SKILL.md definition.
/// </summary>
public sealed class SkillOperation
{
    /// <summary>
    /// Gets the operation identifier, unique within the skill. Becomes the tool-name suffix.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the operation summary surfaced to the model.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets the HTTP method for this operation.
    /// </summary>
    public string HttpMethod { get; init; } = "GET";

    /// <summary>
    /// Gets the HTTP path template. {placeholder} segments are substituted from arguments.
    /// </summary>
    public string HttpPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the declared parameters for this operation.
    /// </summary>
    public IReadOnlyList<SkillOperationParameter> Parameters { get; init; } = [];
}

/// <summary>
/// A parameter declared in a SKILL.md operation.
/// </summary>
public sealed class SkillOperationParameter
{
    /// <summary>
    /// Gets the parameter name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the parameter type: string, integer, number, boolean.
    /// </summary>
    public string Type { get; init; } = "string";

    /// <summary>
    /// Gets the parameter description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the parameter is required.
    /// </summary>
    public bool Required { get; init; }
}