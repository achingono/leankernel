using System.Collections;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Parses SKILL.md files into structured SkillDefinition objects.
/// Extracts YAML frontmatter with typed runtime and operations blocks.
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
    ///   category: financial
    /// runtime:
    ///   type: cli
    ///   command: my-cli
    ///   requires:
    ///     bins:
    ///       - name: my-cli
    ///         minVersion: "1.0"
    ///   egress:
    ///     allowHosts: []
    /// operations:
    ///   - id: my_operation
    ///     summary: "Do something"
    ///     invoke:
    ///       argv: [cmd, subcmd]
    ///     parameters:
    ///       type: object
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
            Examples = ExtractExamples(markdown)
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
            var runtime = ParseRuntime(ExtractDictionary(data, "runtime"));
            var operations = ParseOperations(data["operations"] as IEnumerable);

            var errors = ValidateDefinition(name, description, runtime, operations);

            return new SkillDefinition(
                Name: name,
                Description: description)
            {
                Metadata = metadata,
                Runtime = runtime,
                Operations = operations,
                ValidationErrors = errors
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse frontmatter: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parse runtime block from YAML data.
    /// </summary>
    private SkillRuntime? ParseRuntime(Dictionary<string, object>? runtimeData)
    {
        if (runtimeData == null)
            return null;

        var type = ExtractString(runtimeData, "type") ?? "cli";
        var command = ExtractString(runtimeData, "command");
        var baseUrl = ExtractString(runtimeData, "baseUrl");

        var auth = ParseAuth(ExtractDictionary(runtimeData, "auth"));
        var requires = ParseRequires(ExtractDictionary(runtimeData, "requires"));
        var egress = ParseEgress(ExtractDictionary(runtimeData, "egress"));

        return new SkillRuntime(Type: type, Command: command, BaseUrl: baseUrl)
        {
            Auth = auth ?? new SkillAuth(),
            Requires = requires ?? new SkillRequires(),
            Egress = egress ?? new SkillEgress(),
            TimeoutSeconds = ExtractInt(runtimeData, "timeoutSeconds")
        };
    }

    /// <summary>
    /// Parse auth block from YAML data.
    /// </summary>
    private SkillAuth? ParseAuth(Dictionary<string, object>? authData)
    {
        if (authData == null)
            return null;

        var type = ExtractString(authData, "type") ?? "none";
        var secretRef = ExtractString(authData, "secretRef");

        return new SkillAuth(Type: type, SecretRef: secretRef);
    }

    /// <summary>
    /// Parse requires block from YAML data.
    /// </summary>
    private SkillRequires? ParseRequires(Dictionary<string, object>? requiresData)
    {
        if (requiresData == null)
            return null;

        var bins = ParseBinaries(requiresData["bins"] as IEnumerable);
        return new SkillRequires(Bins: bins);
    }

    /// <summary>
    /// Parse binary requirements list.
    /// </summary>
    private List<BinaryRequirement> ParseBinaries(IEnumerable? binsData)
    {
        var bins = new List<BinaryRequirement>();

        if (binsData == null)
            return bins;

        foreach (var item in binsData)
        {
            var binDict = item as Dictionary<string, object>;
            if (binDict != null)
            {
                var name = ExtractString(binDict, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    bins.Add(new BinaryRequirement(
                        Name: name,
                        MinVersion: ExtractString(binDict, "minVersion"),
                        ChecksumSha256: ExtractString(binDict, "checksumSha256")));
                }
            }
        }

        return bins;
    }

    /// <summary>
    /// Parse egress block from YAML data.
    /// </summary>
    private SkillEgress? ParseEgress(Dictionary<string, object>? egressData)
    {
        if (egressData == null)
            return null;

        var allowHosts = new List<string>();
        if (egressData.TryGetValue("allowHosts", out var hostData) && hostData is IEnumerable hosts)
        {
            foreach (var host in hosts)
            {
                if (host != null)
                    allowHosts.Add(host.ToString()!);
            }
        }

        return new SkillEgress(AllowHosts: allowHosts);
    }

    /// <summary>
    /// Parse operations list from YAML data.
    /// </summary>
    private List<SkillOperation> ParseOperations(IEnumerable? operationsData)
    {
        var operations = new List<SkillOperation>();

        if (operationsData == null)
            return operations;

        foreach (var item in operationsData)
        {
            var opDict = item as Dictionary<string, object>;
            if (opDict != null)
            {
                var id = ExtractString(opDict, "id");
                var summary = ExtractString(opDict, "summary");

                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(summary))
                {
                    var invoke = ParseInvoke(ExtractDictionary(opDict, "invoke"));
                    var parameters = opDict.ContainsKey("parameters") ? (opDict["parameters"] as Dictionary<string, object>) : null;

                    operations.Add(new SkillOperation(Id: id, Summary: summary)
                    {
                        Invoke = invoke,
                        Parameters = parameters
                    });
                }
            }
        }

        return operations;
    }

    /// <summary>
    /// Parse invoke block from YAML data.
    /// </summary>
    private SkillInvoke? ParseInvoke(Dictionary<string, object>? invokeData)
    {
        if (invokeData == null)
            return null;

        var argv = ParseStringList(invokeData["argv"] as IEnumerable);
        var flags = ParseStringDict(invokeData["flags"] as Dictionary<string, object>);
        var httpMethod = ExtractString(invokeData, "httpMethod");
        var httpPath = ExtractString(invokeData, "httpPath");

        return new SkillInvoke(Argv: argv, Flags: flags, HttpMethod: httpMethod, HttpPath: httpPath);
    }

    /// <summary>
    /// Parse string list from YAML data.
    /// </summary>
    private List<string> ParseStringList(IEnumerable? data)
    {
        var result = new List<string>();

        if (data == null)
            return result;

        foreach (var item in data)
        {
            if (item != null)
                result.Add(item.ToString()!);
        }

        return result;
    }

    /// <summary>
    /// Parse string dictionary from YAML data.
    /// </summary>
    private Dictionary<string, string> ParseStringDict(Dictionary<string, object>? data)
    {
        var result = new Dictionary<string, string>();

        if (data == null)
            return result;

        foreach (var kvp in data)
        {
            if (kvp.Value != null)
                result[kvp.Key] = kvp.Value.ToString()!;
        }

        return result;
    }

    /// <summary>
    /// Validate skill definition.
    /// Returns list of validation errors (empty if valid).
    /// </summary>
    private List<string> ValidateDefinition(
        string name,
        string description,
        SkillRuntime? runtime,
        List<SkillOperation> operations)
    {
        var errors = new List<string>();

        if (runtime == null)
            errors.Add("Missing required 'runtime' block");

        if (runtime?.Type == "http" && string.IsNullOrWhiteSpace(runtime.BaseUrl))
            errors.Add("HTTP skill requires 'runtime.baseUrl'");

        if ((runtime?.Type == "cli" || runtime?.Type == "composite") && string.IsNullOrWhiteSpace(runtime?.Command))
            errors.Add("CLI/composite skill requires 'runtime.command'");

        if (runtime?.Type == "http" && runtime.Egress.AllowHosts.Count == 0)
            errors.Add("HTTP skill requires non-empty 'runtime.egress.allowHosts'");

        if (operations.Count == 0)
            errors.Add("At least one operation is required");

        foreach (var op in operations)
        {
            if (op.Invoke == null)
                errors.Add($"Operation '{op.Id}' missing 'invoke' block");
        }

        return errors;
    }

    /// <summary>
    /// Extract examples from bash code blocks.
    /// </summary>
    private static List<SkillExample> ExtractExamples(string markdown)
    {
        var examples = new List<SkillExample>();
        var codeMatches = Regex.Matches(markdown, @"```(?:bash|shell)?\s*\n((?:(?!```).)*?)```", RegexOptions.Singleline);

        foreach (Match match in codeMatches.Cast<Match>().Take(5))
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
    /// Safe extraction of string value from dictionary.
    /// </summary>
    private static string? ExtractString(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
            return value?.ToString();
        return null;
    }

    /// <summary>
    /// Safe extraction of int value from dictionary.
    /// </summary>
    private static int? ExtractInt(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
        {
            if (value is int i)
                return i;
            if (int.TryParse(value?.ToString(), out var parsed))
                return parsed;
        }
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
