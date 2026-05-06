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
    public Dictionary<string, object> Metadata { get; init; } = [];
    public SkillRuntime? Runtime { get; init; }
    public List<SkillOperation> Operations { get; init; } = [];
    public List<SkillExample> Examples { get; init; } = [];
    public string? SourcePath { get; init; }
    public DateTime LoadedAt { get; init; } = DateTime.UtcNow;
    public List<string> ValidationErrors { get; init; } = [];
    public bool IsAvailable { get; init; } = true;
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
    public SkillAuth Auth { get; init; } = new(Type: "none");
    public SkillRequires Requires { get; init; } = new();
    public SkillEgress Egress { get; init; } = new();
    public Dictionary<string, string>? Env { get; init; }
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
    public SkillInvoke? Invoke { get; init; }
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
    public List<string> Argv { get; } = Argv ?? [];
    public Dictionary<string, string> Flags { get; } = Flags ?? [];
}

/// <summary>
/// Example usage extracted from SKILL.md documentation.
/// </summary>
public sealed class SkillExample
{
    public required string Title { get; init; }
    public required string Code { get; init; }
    public string? Description { get; init; }
    public string? Language { get; init; } = "bash";
}
