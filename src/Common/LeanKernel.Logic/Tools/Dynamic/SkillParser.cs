using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

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

    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkillParser"/> class.
    /// </summary>
    public SkillParser()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkillParser"/> class with diagnostics logging.
    /// </summary>
    /// <param name="logger">The logger used to emit parse diagnostics, or null.</param>
    public SkillParser(ILogger? logger)
    {
        _logger = logger;
    }

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
            return MapToDefinition(raw, sourcePath);
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            return null;
        }
    }

    private SkillDefinition? MapToDefinition(RawSkill? raw, string? sourcePath)
    {
        if (raw is null || string.IsNullOrWhiteSpace(raw.Name))
        {
            return null;
        }

        var runtime = raw.Runtime ?? new RawRuntime();

        // Phase 25: accept "http" and "cli"; reject unknown runtime types with a logged reason.
        var runtimeType = (runtime.Type ?? "http").Trim().ToLowerInvariant();
        if (runtimeType is not ("http" or "cli"))
        {
            _logger?.LogWarning(
                "SKILL.md at {Path} uses unsupported runtime type '{RuntimeType}'. Skipping.",
                sourcePath ?? raw.Name,
                runtimeType);
            return null;
        }

        if (runtimeType == "cli" && string.IsNullOrWhiteSpace(runtime.Command))
        {
            _logger?.LogWarning(
                "CLI SKILL.md at {Path} has no runtime.command. Skipping.",
                sourcePath ?? raw.Name);
            return null;
        }

        var allowedHosts = raw.Runtime?.Egress?.AllowHosts ?? [];
        var category = raw.Metadata?.TryGetValue("category", out var cat) == true ? cat?.ToString() : null;

        var operations = BuildOperations(raw, runtimeType, sourcePath);

        if (operations.Count == 0)
        {
            return null;
        }

        return new SkillDefinition
        {
            Name = raw.Name,
            Description = raw.Description ?? string.Empty,
            Category = category,
            Runtime = new SkillRuntimeConfig
            {
                Type = runtimeType,
                BaseUrl = runtimeType == "http" ? (runtime.BaseUrl ?? string.Empty) : string.Empty,
                Command = runtimeType == "cli" ? (runtime.Command ?? string.Empty) : string.Empty,
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

    private List<SkillOperation> BuildOperations(RawSkill raw, string runtimeType, string? sourcePath)
    {
        var operations = new List<SkillOperation>();

        foreach (var o in raw.Operations ?? [])
        {
            if (string.IsNullOrWhiteSpace(o.Id))
            {
                continue;
            }

            var invoke = o.Invoke ?? new RawInvoke();
            var parameters = ConvertParameters(o.Parameters);

            if (runtimeType == "cli")
            {
                if (!ValidateCliOperation(raw.Name!, o.Id!, invoke, parameters, sourcePath))
                {
                    continue;
                }
            }

            operations.Add(new SkillOperation
            {
                Id = o.Id!,
                Summary = o.Summary ?? string.Empty,
                HttpMethod = (invoke.HttpMethod ?? "GET").ToUpperInvariant(),
                HttpPath = invoke.HttpPath ?? string.Empty,
                Argv = invoke.Argv ?? [],
                Flags = new Dictionary<string, string?>(invoke.Flags ?? new Dictionary<string, string?>(), StringComparer.Ordinal),
                Parameters = parameters
            });
        }

        return operations;
    }

    private bool ValidateCliOperation(
        string skillName,
        string operationId,
        RawInvoke invoke,
        List<SkillOperationParameter> parameters,
        string? sourcePath)
    {
        var declared = parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        if (invoke.Flags is not null)
        {
            foreach (var flag in invoke.Flags)
            {
                // Null flag values (bare positional args) are resolved at runtime and need no declared flag.
                if (string.IsNullOrWhiteSpace(flag.Value))
                {
                    continue;
                }

                if (!declared.Contains(flag.Key))
                {
                    _logger?.LogWarning(
                        "CLI skill '{Skill}' at {Path} operation '{Operation}' maps flag for undeclared parameter '{Parameter}'. Skipping operation.",
                        skillName, sourcePath ?? skillName, operationId, flag.Key);
                    return false;
                }
            }
        }

        return true;
    }

    private static List<SkillOperationParameter> ConvertParameters(object? raw)
    {
        if (raw is not Dictionary<object, object?> dict)
        {
            return [];
        }

        // JSON-schema style: {type: object, properties: {...}, required: [...]}
        if (dict.TryGetValue("properties", out var propsObj) && propsObj is Dictionary<object, object?> props)
        {
            var required = new HashSet<string>(StringComparer.Ordinal);
            if (dict.TryGetValue("required", out var requiredObj) && requiredObj is IEnumerable<object> requiredList)
            {
                foreach (var r in requiredList)
                {
                    if (r is string s)
                    {
                        required.Add(s);
                    }
                }
            }

            var schemaParameters = new List<SkillOperationParameter>();
            foreach (var prop in props)
            {
                var name = prop.Key?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var (type, description) = ParseParameterMetadata(prop.Value);
                schemaParameters.Add(new SkillOperationParameter
                {
                    Name = name,
                    Type = type,
                    Description = description,
                    Required = required.Contains(name)
                });
            }

            return schemaParameters;
        }

        // Flat format: {name: {type, description, required}}
        var parameters = new List<SkillOperationParameter>();
        foreach (var kvp in dict)
        {
            var name = kvp.Key?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (kvp.Value is not Dictionary<object, object?> meta)
            {
                parameters.Add(new SkillOperationParameter
                {
                    Name = name,
                    Type = "string",
                    Description = string.Empty
                });
                continue;
            }

            var type = meta.TryGetValue("type", out var typeVal) ? typeVal?.ToString() ?? "string" : "string";
            var description = meta.TryGetValue("description", out var descVal) ? descVal?.ToString() ?? string.Empty : string.Empty;
            var isRequired = meta.TryGetValue("required", out var reqVal)
                && (reqVal is bool reqBool
                    ? reqBool
                    : reqVal?.ToString() is string reqText && bool.TryParse(reqText, out var parsed) && parsed);

            parameters.Add(new SkillOperationParameter
            {
                Name = name,
                Type = type,
                Description = description,
                Required = isRequired
            });
        }

        return parameters;
    }

    private static (string Type, string Description) ParseParameterMetadata(object? value)
    {
        if (value is not Dictionary<object, object?> meta)
        {
            return ("string", string.Empty);
        }

        var type = meta.TryGetValue("type", out var typeVal) ? typeVal?.ToString() ?? "string" : "string";
        var description = meta.TryGetValue("description", out var descVal) ? descVal?.ToString() ?? string.Empty : string.Empty;
        return (type, description);
    }
}