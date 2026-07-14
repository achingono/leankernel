using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LeanKernel.Gateway.Tools.Dynamic;

/// <summary>
/// Parses SKILL.md files with YAML frontmatter into <see cref="SkillDefinition"/> instances.
/// </summary>
public sealed partial class SkillParser
{
    [GeneratedRegex(@"^---\s*\r?\n(.*?)\r?\n---", RegexOptions.Singleline)]
    private static partial Regex FrontmatterPattern();

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>
    /// Parses a SKILL.md file from the specified path.
    /// Returns null when the file does not exist or is invalid.
    /// </summary>
    public SkillDefinition? Parse(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            return null;
        }

        var content = File.ReadAllText(filePath);
        return ParseContent(content, filePath);
    }

    /// <summary>
    /// Parses a SKILL.md from raw content string.
    /// </summary>
    public SkillDefinition? ParseContent(string content, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var match = FrontmatterPattern().Match(content);
        if (!match.Success)
        {
            return null;
        }

        var yaml = match.Groups[1].Value;

        try
        {
            var raw = _deserializer.Deserialize<RawSkill>(yaml);
            return MapToDefinition(raw, sourcePath);
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            return null;
        }
    }

    private static SkillDefinition? MapToDefinition(RawSkill? raw, string? sourcePath)
    {
        if (raw is null || string.IsNullOrWhiteSpace(raw.Name))
        {
            return null;
        }

        var runtime = raw.Runtime ?? new RawRuntime();

        // Phase 01: reject non-http types
        var runtimeType = (runtime.Type ?? "http").Trim().ToLowerInvariant();
        if (runtimeType != "http")
        {
            return null;
        }

        var allowedHosts = raw.Runtime?.Egress?.AllowHosts ?? [];

        var operations = raw.Operations?
            .Where(o => !string.IsNullOrWhiteSpace(o.Id))
            .Select(o => new SkillOperation
            {
                Id = o.Id!,
                Summary = o.Summary ?? string.Empty,
                HttpMethod = (o.Invoke?.HttpMethod ?? "GET").ToUpperInvariant(),
                HttpPath = o.Invoke?.HttpPath ?? string.Empty,
                Parameters = o.Parameters?
                    .Select(p => new SkillOperationParameter
                    {
                        Name = p.Key,
                        Type = p.Value?.Type ?? "string",
                        Description = p.Value?.Description ?? string.Empty,
                        Required = p.Value?.Required ?? false
                    })
                    .ToList() ?? []
            })
            .ToList() ?? [];

        if (operations.Count == 0)
        {
            return null;
        }

        return new SkillDefinition
        {
            Name = raw.Name,
            Description = raw.Description ?? string.Empty,
            Category = raw.Metadata?.TryGetValue("category", out var cat) == true ? cat?.ToString() : null,
            Runtime = new SkillRuntimeConfig
            {
                Type = runtimeType,
                BaseUrl = runtime.BaseUrl ?? string.Empty,
                TimeoutSeconds = runtime.TimeoutSeconds > 0 ? runtime.TimeoutSeconds : 30,
                Auth = new SkillAuthConfig
                {
                    Type = (runtime.Auth?.Type ?? "none").ToLowerInvariant(),
                    SecretRef = runtime.Auth?.SecretRef
                }
            },
            AllowedHosts = allowedHosts,
            Operations = operations
        };
    }
}

// Raw YAML DTOs (for deserialization only)
#pragma warning disable CS8618
internal sealed class RawSkill
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, object?>? Metadata { get; set; }
    public RawRuntime? Runtime { get; set; }
    public List<RawOperation>? Operations { get; set; }
}

internal sealed class RawRuntime
{
    public string? Type { get; set; }
    public string? BaseUrl { get; set; }
    public int TimeoutSeconds { get; set; }
    public RawAuth? Auth { get; set; }
    public RawEgress? Egress { get; set; }
}

internal sealed class RawAuth
{
    public string? Type { get; set; }
    public string? SecretRef { get; set; }
}

internal sealed class RawEgress
{
    public List<string>? AllowHosts { get; set; }
}

internal sealed class RawOperation
{
    public string? Id { get; set; }
    public string? Summary { get; set; }
    public RawInvoke? Invoke { get; set; }
    public Dictionary<string, RawParameter?>? Parameters { get; set; }
}

internal sealed class RawInvoke
{
    public string? HttpMethod { get; set; }
    public string? HttpPath { get; set; }
}

internal sealed class RawParameter
{
    public string? Type { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; }
}
#pragma warning restore CS8618
