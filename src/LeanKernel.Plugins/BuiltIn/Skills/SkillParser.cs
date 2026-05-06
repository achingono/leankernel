using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Parses SKILL.md files into structured SkillDefinition objects.
/// Extracts YAML frontmatter and documentation into a loadable format.
/// </summary>
public sealed class SkillParser
{
    private readonly IDeserializer _yamlDeserializer;

    public SkillParser()
    {
        _yamlDeserializer = new DeserializerBuilder()
            .WithAttemptingUnquotedStringTypeDeserialization()
            .Build();
    }

    /// <summary>
    /// Parse a SKILL.md file into a SkillDefinition.
    /// Frontmatter format:
    /// ---
    /// name: skill_name
    /// description: "..."
    /// metadata:
    ///   emoji: "📢"
    ///   homepage: "https://..."
    ///   baseUrl: "http://..."
    ///   cliCommand: "my-cli"
    ///   authType: "none"
    /// ---
    /// </summary>
    public async Task<SkillDefinition?> ParseSkillFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            return ParseSkillContent(content, filePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse skill file {filePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parse skill content directly (useful for testing).
    /// </summary>
    public SkillDefinition? ParseSkillContent(string content, string? sourcePath = null)
    {
        var (frontmatter, markdown) = ExtractFrontmatter(content);
        if (frontmatter == null)
            return null;

        var definition = ParseFrontmatter(frontmatter);
        if (definition == null)
            return null;

        definition = definition with
        {
            SourcePath = sourcePath,
            Operations = ExtractOperations(markdown, definition.BaseUrl, definition.CliCommand),
            Examples = ExtractExamples(markdown),
            ParametersSchema = ExtractParametersSchema(markdown)
        };

        return definition;
    }

    /// <summary>
    /// Extract YAML frontmatter (between --- markers) from content.
    /// </summary>
    private static (string? frontmatter, string markdown) ExtractFrontmatter(string content)
    {
        var lines = content.Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != "---")
            return (null, content);

        var endIdx = Array.FindIndex(lines, 1, line => line.Trim() == "---");
        if (endIdx <= 0)
            return (null, content);

        var frontmatter = string.Join('\n', lines[1..endIdx]);
        var markdown = string.Join('\n', lines[(endIdx + 1)..]);
        return (frontmatter, markdown);
    }

    /// <summary>
    /// Parse YAML frontmatter into SkillDefinition.
    /// </summary>
    private SkillDefinition? ParseFrontmatter(string frontmatter)
    {
        try
        {
            var data = _yamlDeserializer.Deserialize<Dictionary<string, object>>(frontmatter);
            if (data == null)
                return null;

            var name = ExtractString(data, "name");
            var description = ExtractString(data, "description");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
                return null;

            var metadata = ExtractDictionary(data, "metadata") ?? [];

            var operationType = DetermineOperationType(metadata);

            return new SkillDefinition(
                Name: name,
                Description: description,
                OperationType: operationType)
            {
                BaseUrl = ExtractString(metadata, "baseUrl"),
                CliCommand = ExtractString(metadata, "cliCommand"),
                AuthType = ExtractString(metadata, "authType") ?? "none",
                Emoji = ExtractString(metadata, "emoji"),
                Homepage = ExtractString(metadata, "homepage"),
                Metadata = metadata
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse frontmatter: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extract operations from markdown documentation.
    /// Looks for "## Operation:" sections or curl examples.
    /// </summary>
    private static Dictionary<string, SkillOperation> ExtractOperations(
        string markdown,
        string? baseUrl,
        string? cliCommand)
    {
        var operations = new Dictionary<string, SkillOperation>();

        // Extract operations from "### " sections
        var operationMatches = Regex.Matches(markdown, @"###\s+([^\n]+)\n((?:(?!###|##).)*)", RegexOptions.Singleline);

        foreach (Match match in operationMatches)
        {
            var opName = match.Groups[1].Value.Trim().ToLowerInvariant().Replace(" ", "_");
            var opContent = match.Groups[2].Value;

            var operation = ExtractOperation(opName, opContent);
            if (operation != null)
                operations[operation.Name] = operation;
        }

        return operations;
    }

    /// <summary>
    /// Extract a single operation from its documentation block.
    /// </summary>
    private static SkillOperation? ExtractOperation(string name, string content)
    {
        // Extract endpoint from code blocks
        var endpoint = ExtractEndpoint(content);
        var httpMethod = DetermineHttpMethod(content);
        var requiredParams = ExtractParams(content, isRequired: true);
        var optionalParams = ExtractParams(content, isRequired: false);

        return new SkillOperation
        {
            Name = name,
            Description = ExtractFirstLine(content),
            Endpoint = endpoint,
            HttpMethod = httpMethod,
            RequiredParams = requiredParams,
            OptionalParams = optionalParams
        };
    }

    /// <summary>
    /// Extract examples from bash code blocks.
    /// </summary>
    private static List<SkillExample> ExtractExamples(string markdown)
    {
        var examples = new List<SkillExample>();
        var codeMatches = Regex.Matches(markdown, @"```(?:bash|shell)?\s*\n((?:(?!```).)*?)```", RegexOptions.Singleline);

        foreach (Match match in codeMatches.Cast<Match>().Take(5)) // Limit to 5 examples
        {
            var code = match.Groups[1].Value.Trim();
            if (code.StartsWith("curl"))
            {
                examples.Add(new SkillExample
                {
                    Title = $"Example {examples.Count + 1}",
                    Code = code,
                    Language = "bash"
                });
            }
        }

        return examples;
    }

    /// <summary>
    /// Extract JSON Schema parameters section.
    /// </summary>
    private static string? ExtractParametersSchema(string markdown)
    {
        var match = Regex.Match(markdown, @"```json\s*\n(\{\s*""type"":.*?\})\s*```", RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Determine operation type from metadata.
    /// </summary>
    private static string DetermineOperationType(Dictionary<string, object> metadata)
    {
        if (ExtractString(metadata, "baseUrl") != null)
            return "http";
        if (ExtractString(metadata, "cliCommand") != null)
            return "cli";
        return "composite";
    }

    /// <summary>
    /// Extract HTTP method from curl example (GET, POST, PATCH, DELETE).
    /// </summary>
    private static string DetermineHttpMethod(string content)
    {
        if (content.Contains("-X POST", StringComparison.OrdinalIgnoreCase))
            return "POST";
        if (content.Contains("-X PATCH", StringComparison.OrdinalIgnoreCase))
            return "PATCH";
        if (content.Contains("-X DELETE", StringComparison.OrdinalIgnoreCase))
            return "DELETE";
        return "GET";
    }

    /// <summary>
    /// Extract endpoint from curl command.
    /// </summary>
    private static string? ExtractEndpoint(string content)
    {
        var match = Regex.Match(content, @"curl[^""]+""\s*([^""\s]+)""");
        if (match.Success)
            return match.Groups[1].Value;

        match = Regex.Match(content, @"curl\s+([^\s]+)");
        if (match.Success)
            return match.Groups[1].Value;

        return null;
    }

    /// <summary>
    /// Extract parameter names from documentation.
    /// </summary>
    private static List<string> ExtractParams(string content, bool isRequired)
    {
        var marker = isRequired ? "required" : "optional";
        var pattern = $@"- `([^`]+)`.*?{marker}";
        var results = new List<string>();

        foreach (Match match in Regex.Matches(content, pattern, RegexOptions.IgnoreCase))
            results.Add(match.Groups[1].Value);

        return results;
    }

    /// <summary>
    /// Extract first line of text (for description).
    /// </summary>
    private static string ExtractFirstLine(string text)
    {
        var line = text.Split('\n')[0].Trim();
        return line.Length > 100 ? line[..100] + "..." : line;
    }

    /// <summary>
    /// Safe extraction of string value from dictionary.
    /// </summary>
    private static string? ExtractString(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
            return value?.ToString();
        return null;
    }

    /// <summary>
    /// Safe extraction of nested dictionary.
    /// </summary>
    private static Dictionary<string, object>? ExtractDictionary(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value) && value is Dictionary<string, object> nested)
            return nested;
        return null;
    }
}
