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
            // YamlDotNet deserializes to Dictionary<object, object>, need to normalize to Dictionary<string, object>
            var rawData = _yamlDeserializer.Deserialize<Dictionary<object, object>>(frontmatter);
            if (rawData == null)
                return null;

            var data = NormalizeDictionary(rawData);
            if (data == null)
                return null;

            var name = ExtractString(data, "name");
            var description = ExtractString(data, "description");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
                return null;

            var metadata = ExtractDictionary(data, "metadata") ?? [];
            var runtime = ParseRuntime(ExtractDictionary(data, "runtime"));
            var operations = ParseOperations(data.TryGetValue("operations", out var opsData) ? opsData as IEnumerable : null);

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

        var argv = invokeData.TryGetValue("argv", out var argvData) ? ParseStringList(argvData as IEnumerable) : [];
        var flags = invokeData.TryGetValue("flags", out var flagsData) ? ParseStringDict(flagsData as Dictionary<string, object>) : [];
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
            else
                ValidateOperation(op, errors);

            if (op.Parameters != null)
                ValidateJsonSchema(op.Id, op.Parameters, errors);
        }

        if (runtime?.Requires.Bins.Count > 0)
            ValidateBinaryRequirements(runtime.Requires.Bins, errors);

        return errors;
    }

    /// <summary>
    /// Validate a single operation's structure and flags.
    /// </summary>
    private void ValidateOperation(SkillOperation op, List<string> errors)
    {
        if (op.Invoke == null)
            return;

        // For CLI/composite operations (no httpMethod/httpPath), validate argv is not empty
        if (string.IsNullOrEmpty(op.Invoke.HttpMethod) && op.Invoke.Argv.Count == 0)
            errors.Add($"Operation '{op.Id}' has empty argv for CLI/composite operation");

        // Validate that all flags map to documented parameters
        if (op.Invoke.Flags.Count > 0 && op.Parameters != null)
        {
            var paramProps = ExtractParameterProperties(op.Parameters);
            foreach (var flagName in op.Invoke.Flags.Keys)
            {
                if (!paramProps.Contains(flagName))
                    errors.Add($"Operation '{op.Id}' flag '{flagName}' not declared in parameters");
            }
        }
    }

    /// <summary>
    /// Extract property names from a JSON Schema object.
    /// </summary>
    private HashSet<string> ExtractParameterProperties(Dictionary<string, object> schema)
    {
        var props = new HashSet<string>();

        if (schema.TryGetValue("properties", out var propObj) && propObj is Dictionary<string, object> properties)
        {
            foreach (var key in properties.Keys)
                props.Add(key);
        }

        return props;
    }

    /// <summary>
    /// Validate JSON Schema structure.
    /// </summary>
    private void ValidateJsonSchema(string operationId, Dictionary<string, object> schema, List<string> errors)
    {
        if (!schema.ContainsKey("type"))
            errors.Add($"Operation '{operationId}' parameters missing 'type' field");

        if (schema.TryGetValue("type", out var typeObj) && typeObj?.ToString() != "object")
            errors.Add($"Operation '{operationId}' parameters type must be 'object'");
    }

    /// <summary>
    /// Validate binary requirements.
    /// </summary>
    private void ValidateBinaryRequirements(List<BinaryRequirement> bins, List<string> errors)
    {
        foreach (var bin in bins)
        {
            if (string.IsNullOrWhiteSpace(bin.Name))
                errors.Add("Binary requirement missing 'name'");

            if (!string.IsNullOrWhiteSpace(bin.ChecksumSha256) && !IsValidSha256(bin.ChecksumSha256))
                errors.Add($"Binary '{bin.Name}' has invalid SHA256 checksum format");
        }
    }

    /// <summary>
    /// Check if a string is a valid SHA256 hex string.
    /// </summary>
    private bool IsValidSha256(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        return hex.Length == 64 && hex.All(c => "0123456789abcdefABCDEF".Contains(c));
    }

    /// <summary>
    /// Extract examples from bash code blocks.
    /// </summary>
    private static Dictionary<string, object>? NormalizeDictionary(Dictionary<object, object>? dict)
    {
        if (dict == null)
            return null;

        var normalized = new Dictionary<string, object>();
        foreach (var kvp in dict)
        {
            var key = kvp.Key?.ToString() ?? "";
            if (string.IsNullOrEmpty(key))
                continue;

            object? value = kvp.Value;

            // Recursively normalize nested dictionaries
            if (value is Dictionary<object, object> nestedDict)
            {
                value = NormalizeDictionary(nestedDict);
            }
            // Convert lists of dictionaries
            else if (value is List<object> list)
            {
                var normalizedList = new List<object>();
                foreach (var item in list)
                {
                    if (item is Dictionary<object, object> itemDict)
                    {
                        normalizedList.Add(NormalizeDictionary(itemDict) ?? item);
                    }
                    else
                    {
                        normalizedList.Add(item);
                    }
                }
                value = normalizedList;
            }

            normalized[key] = value!;
        }

        return normalized;
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
