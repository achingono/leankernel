using System.Text.Json.Serialization;

namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Represents a skill definition parsed from SKILL.md file with frontmatter.
/// Skills define operations via HTTP endpoints or CLI commands.
/// </summary>
public sealed record SkillDefinition(
    string Name,
    string Description)
{
    /// <summary>
    /// Gets or sets the metadata.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
    /// <summary>
    /// Gets or sets the runtime.
    /// </summary>
    public SkillRuntime? Runtime { get; init; }
    /// <summary>
    /// Gets or sets the operations.
    /// </summary>
    public List<SkillOperation> Operations { get; init; } = [];
    /// <summary>
    /// Gets or sets the examples.
    /// </summary>
    public List<SkillExample> Examples { get; init; } = [];
    /// <summary>
    /// Gets or sets the source path.
    /// </summary>
    public string? SourcePath { get; init; }
    /// <summary>
    /// Gets or sets the loaded at.
    /// </summary>
    public DateTime LoadedAt { get; init; } = DateTime.UtcNow;
    /// <summary>
    /// Gets or sets the validation errors.
    /// </summary>
    public List<string> ValidationErrors { get; init; } = [];
    /// <summary>
    /// Gets or sets the is available.
    /// </summary>
    public bool IsAvailable { get; init; } = true;
    /// <summary>
    /// Gets or sets the unavailable reason.
    /// </summary>
    public string? UnavailableReason { get; init; }
}

/// <summary>
/// Runtime configuration for a skill.
/// Defines type (cli/http/composite), command, auth, binary requirements, and egress policy.
/// </summary>
public sealed record SkillRuntime(
    string Type,
    string? Command = null,
    string? BaseUrl = null)
{
    /// <summary>
    /// Gets or sets the auth.
    /// </summary>
    public SkillAuth Auth { get; init; } = new(Type: "none");
    /// <summary>
    /// Gets or sets the requires.
    /// </summary>
    public SkillRequires Requires { get; init; } = new();
    /// <summary>
    /// Gets or sets the egress.
    /// </summary>
    public SkillEgress Egress { get; init; } = new();
    /// <summary>
    /// Gets or sets the env.
    /// </summary>
    public Dictionary<string, string>? Env { get; init; }
    /// <summary>
    /// Gets or sets the timeout seconds.
    /// </summary>
    public int? TimeoutSeconds { get; init; }
}

/// <summary>
/// Authentication configuration for a skill.
/// </summary>
public sealed record SkillAuth(
    string Type = "none",
    string? SecretRef = null);

/// <summary>
/// Binary requirements for a skill.
/// </summary>
public sealed record SkillRequires(
    List<BinaryRequirement>? Bins = null)
{
    /// <summary>
    /// Gets or sets the bins.
    /// </summary>
    public List<BinaryRequirement> Bins { get; } = Bins ?? [];
}

/// <summary>
/// Specification for a required binary/executable.
/// </summary>
public sealed record BinaryRequirement(
    string Name,
    string? MinVersion = null,
    string? ChecksumSha256 = null);

/// <summary>
/// Egress policy for HTTP skills.
/// </summary>
public sealed record SkillEgress(
    List<string>? AllowHosts = null)
{
    /// <summary>
    /// Gets or sets the allow hosts.
    /// </summary>
    public List<string> AllowHosts { get; } = AllowHosts ?? [];
}

/// <summary>
/// Represents a single operation within a skill.
/// Defines how to invoke the operation and what parameters it accepts.
/// </summary>
public sealed record SkillOperation(
    string Id,
    string Summary)
{
    /// <summary>
    /// Gets or sets the invoke.
    /// </summary>
    public SkillInvoke? Invoke { get; init; }
    /// <summary>
    /// Gets or sets the parameters.
    /// </summary>
    public Dictionary<string, object>? Parameters { get; init; }
}

/// <summary>
/// Invocation details for an operation (argv or HTTP endpoint).
/// </summary>
public sealed record SkillInvoke(
    List<string>? Argv = null,
    Dictionary<string, string>? Flags = null,
    string? HttpMethod = null,
    string? HttpPath = null)
{
    /// <summary>
    /// Gets or sets the argv.
    /// </summary>
    public List<string> Argv { get; } = Argv ?? [];
    /// <summary>
    /// Gets or sets the flags.
    /// </summary>
    public Dictionary<string, string> Flags { get; } = Flags ?? [];
}

/// <summary>
/// Example usage extracted from SKILL.md documentation.
/// </summary>
public sealed class SkillExample
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public required string Title { get; init; }
    /// <summary>
    /// Gets or sets the code.
    /// </summary>
    public required string Code { get; init; }
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; init; }
    /// <summary>
    /// Gets or sets the language.
    /// </summary>
    public string? Language { get; init; } = "bash";
}
