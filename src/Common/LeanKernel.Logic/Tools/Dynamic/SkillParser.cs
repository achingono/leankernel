using System.Text.RegularExpressions;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LeanKernel.Logic.Tools.Dynamic;

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
    /// <param name="filePath">The path to the SKILL.md file.</param>
    /// <returns>The parsed <see cref="SkillDefinition"/>, or null if parsing fails.</returns>
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
    /// <param name="content">The raw content of the SKILL.md file.</param>
    /// <param name="sourcePath">Optional source path for diagnostics.</param>
    /// <returns>The parsed <see cref="SkillDefinition"/>, or null if parsing fails.</returns>
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
            return MapToDefinition(raw);
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            return null;
        }
    }

    private static SkillDefinition? MapToDefinition(RawSkill? raw)
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
#pragma warning restore CS8618