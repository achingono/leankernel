namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Represents the definition of a skill.
/// </summary>
public sealed record SkillDefinition
{
    /// <summary>
    /// Gets the unique name of the skill.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a description of the skill.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the metadata associated with the skill.
    /// </summary>
    public Dictionary<string, object?> Metadata { get; init; } = new();

    /// <summary>
    /// Gets the runtime configuration for the skill.
    /// </summary>
    public required SkillRuntimeConfig Runtime { get; init; }

    /// <summary>
    /// Gets the operations provided by the skill.
    /// </summary>
    public required IReadOnlyList<SkillOperation> Operations { get; init; }

    /// <summary>
    /// Gets the source path for the skill, if any.
    /// </summary>
    public string? SourcePath { get; init; }
}

/// <summary>
/// Configures the runtime execution of a skill.
/// </summary>
public sealed record SkillRuntimeConfig
{
    /// <summary>
    /// Gets the execution type of the skill (e.g., "cli").
    /// </summary>
    public string Type { get; init; } = "cli";

    /// <summary>
    /// Gets the command to execute for CLI-based skills.
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Gets the base URL for web-based skills.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Gets the timeout in seconds for skill execution.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets the authentication configuration for the skill.
    /// </summary>
    public SkillAuthConfig Auth { get; init; } = new();

    /// <summary>
    /// Gets the requirements for the skill runtime.
    /// </summary>
    public SkillRequiresConfig Requires { get; init; } = new();

    /// <summary>
    /// Gets the egress configuration for the skill.
    /// </summary>
    public SkillEgressConfig Egress { get; init; } = new();
}

/// <summary>
/// Configures authentication for a skill.
/// </summary>
public sealed record SkillAuthConfig
{
    /// <summary>
    /// Gets the authentication type.
    /// </summary>
    public string Type { get; init; } = "none";

    /// <summary>
    /// Gets the reference to a secret for the authentication.
    /// </summary>
    public string? SecretRef { get; init; }
}

/// <summary>
/// Defines the requirements for the skill runtime.
/// </summary>
public sealed record SkillRequiresConfig
{
    /// <summary>
    /// Gets the list of required bins/tools.
    /// </summary>
    public IReadOnlyList<SkillBinConfig> Bins { get; init; } = Array.Empty<SkillBinConfig>();
}

/// <summary>
/// Represents a required binary for the skill runtime.
/// </summary>
public sealed record SkillBinConfig
{
    /// <summary>
    /// Gets the name of the binary.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the minimum version of the binary.
    /// </summary>
    public string? MinVersion { get; init; }

    /// <summary>
    /// Gets the SHA256 checksum for the binary.
    /// </summary>
    public string? ChecksumSha256 { get; init; }
}

/// <summary>
/// Configures the egress rules for a skill.
/// </summary>
public sealed record SkillEgressConfig
{
    /// <summary>
    /// Gets the list of allowed hosts.
    /// </summary>
    public IReadOnlyList<string> AllowHosts { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Represents an operation that can be performed by a skill.
/// </summary>
public sealed record SkillOperation
{
    /// <summary>
    /// Gets the unique identifier of the operation.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets a summary of the operation.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Gets the invocation configuration for the operation.
    /// </summary>
    public SkillInvokeConfig Invoke { get; init; } = new();

    /// <summary>
    /// Gets the raw parameters for the operation.
    /// </summary>
    public Dictionary<string, object?>? ParametersRaw { get; init; }
}

/// <summary>
/// Configures how an operation is invoked.
/// </summary>
public sealed record SkillInvokeConfig
{
    /// <summary>
    /// Gets the list of arguments.
    /// </summary>
    public IReadOnlyList<string> Argv { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the dictionary of flags.
    /// </summary>
    public Dictionary<string, string> Flags { get; init; } = new();

    /// <summary>
    /// Gets the HTTP method for web-based invocations.
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Gets the HTTP path for web-based invocations.
    /// </summary>
    public string? HttpPath { get; init; }
}
