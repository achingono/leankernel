namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Represents a skill definition parsed from SKILL.md file with frontmatter.
/// Skills define operations via HTTP endpoints or CLI commands.
/// </summary>
public sealed record SkillDefinition(
    string Name,
    string Description,
    string OperationType)
{
    public string? Category { get; init; }
    public string? Emoji { get; init; }
    public string? Homepage { get; init; }
    public string? BaseUrl { get; init; }
    public string? CliCommand { get; init; }
    public string AuthType { get; init; } = "none";
    public Dictionary<string, SkillOperation> Operations { get; init; } = [];
    public string? ParametersSchema { get; init; }
    public List<SkillExample> Examples { get; init; } = [];
    public Dictionary<string, object> Metadata { get; init; } = [];
    public string? SourcePath { get; init; }
    public DateTime LoadedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a single operation within a skill.
/// </summary>
public sealed class SkillOperation
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>HTTP endpoint or CLI subcommand</summary>
    public string? Endpoint { get; init; }

    /// <summary>HTTP method (GET, POST, PATCH, DELETE)</summary>
    public string HttpMethod { get; init; } = "GET";

    /// <summary>Request body template or CLI args</summary>
    public string? RequestTemplate { get; init; }

    /// <summary>Response transformation or parsing logic</summary>
    public string? ResponseParser { get; init; }

    /// <summary>Required parameters</summary>
    public List<string> RequiredParams { get; init; } = [];

    /// <summary>Optional parameters</summary>
    public List<string> OptionalParams { get; init; } = [];
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
