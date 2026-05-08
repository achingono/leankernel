using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Plugins.BuiltIn.Skills;

/// <summary>
/// Dynamic skill tool that wraps any loaded skill definition.
/// Invokes skills via HTTP, CLI, or composite methods at runtime.
/// No compilation required — just add a SKILL.md file to the skill directory.
/// </summary>
public sealed class DynamicSkillTool : ITool, IOperationsTool
{
    private readonly SkillDefinition _skillDef;
    private readonly HttpClient _httpClient;
    private readonly IBinaryResolver _binaryResolver;
    private readonly ILogger<DynamicSkillTool> _logger;

    public string Name => NormalizeToolName(_skillDef.Name);
    public string Description => _skillDef.Description;
    public string Category => GetCategory();
    public string ParametersSchema => BuildParametersSchema();

    public IReadOnlyList<ToolOperationDescriptor> Operations =>
        _skillDef.Operations
            .Select(op => new ToolOperationDescriptor(op.Id, op.Summary, BuildOperationParametersSchema(op)))
            .ToList();

    public DynamicSkillTool(
        SkillDefinition skillDef,
        HttpClient httpClient,
        IBinaryResolver binaryResolver,
        ILogger<DynamicSkillTool> logger)
    {
        _skillDef = skillDef;
        _httpClient = httpClient;
        _binaryResolver = binaryResolver;
        _logger = logger;
    }

    /// <summary>
    /// Normalize skill tool names by removing the "_skill" suffix.
    /// Example: "ms-todo_skill" -> "ms-todo"
    /// </summary>
    private static string NormalizeToolName(string name)
    {
        if (name.EndsWith("_skill", StringComparison.OrdinalIgnoreCase))
            return name[..^6]; // Remove last 6 characters
        return name;
    }

    /// <summary>
    /// Get the category from skill metadata or use a default.
    /// </summary>
    private string GetCategory()
    {
        if (_skillDef.Metadata.TryGetValue("category", out var category))
            return category?.ToString() ?? "general";
        return "general";
    }

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (!_skillDef.IsAvailable)
                return FailResult(sw, $"Skill is unavailable: {_skillDef.UnavailableReason ?? "unknown reason"}");

            if (_skillDef.Runtime == null)
                return FailResult(sw, "Skill has no runtime configuration");

            using var doc = JsonDocument.Parse(parametersJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("operation", out var opProp))
                return FailResult(sw, "Missing required 'operation' parameter");

            var operationId = opProp.GetString() ?? "";
            var op = _skillDef.Operations.FirstOrDefault(o => o.Id == operationId);

            if (op == null)
                return FailResult(sw, $"Unknown operation '{operationId}'. Available: {string.Join(", ", _skillDef.Operations.Select(o => o.Id))}");

            if (op.Invoke == null)
                return FailResult(sw, $"Operation '{operationId}' has no invoke configuration");

            var result = _skillDef.Runtime.Type switch
            {
                "http" => await ExecuteHttpOperation(op, root, ct),
                "cli" => await ExecuteCliOperation(op, root, ct),
                "composite" => await ExecuteCompositeOperation(op, root, ct),
                _ => $"Unsupported operation type: {_skillDef.Runtime.Type}"
            };

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = result,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing skill {SkillName}", _skillDef.Name);
            return FailResult(sw, ex.Message);
        }
    }

    private async Task<string> ExecuteHttpOperation(SkillOperation op, JsonElement root, CancellationToken ct)
    {
        if (_skillDef.Runtime?.BaseUrl == null || op.Invoke?.HttpPath == null)
            return "HTTP operation requires BaseUrl and httpPath";

        var url = _skillDef.Runtime.BaseUrl.TrimEnd('/') + op.Invoke.HttpPath;

        try
        {
            var method = op.Invoke.HttpMethod ?? "GET";
            HttpResponseMessage response = method switch
            {
                "GET" => await _httpClient.GetAsync(url, ct),
                "POST" => await _httpClient.PostAsync(url, BuildRequestContent(root), ct),
                "PATCH" => await _httpClient.PatchAsync(url, BuildRequestContent(root), ct),
                "DELETE" => await _httpClient.DeleteAsync(url, ct),
                _ => throw new NotSupportedException($"HTTP method {method} not supported")
            };

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            return $"HTTP request failed: {ex.Message}";
        }
    }

    private async Task<string> ExecuteCliOperation(SkillOperation op, JsonElement root, CancellationToken ct)
    {
        if (_skillDef.Runtime?.Command == null)
            return "CLI operation requires runtime.command";

        if (op.Invoke?.Argv == null || op.Invoke.Argv.Count == 0)
            return "CLI operation requires invoke.argv";

        // Resolve binary path
        var binaryPath = _binaryResolver.ResolveBinary(_skillDef.Runtime.Command);
        if (binaryPath == null)
            return $"Binary '{_skillDef.Runtime.Command}' not found (check requires.bins)";

        var args = BuildCliArgs(op, root);
        return await ExecuteCommand(binaryPath, args, ct);
    }

    private async Task<string> ExecuteCompositeOperation(SkillOperation op, JsonElement root, CancellationToken ct)
    {
        // Try HTTP first, then fall back to CLI
        if (_skillDef.Runtime?.BaseUrl != null)
        {
            try
            {
                return await ExecuteHttpOperation(op, root, ct);
            }
            catch { /* fall through to CLI */ }
        }

        if (_skillDef.Runtime?.Command != null)
        {
            return await ExecuteCliOperation(op, root, ct);
        }

        return "Composite operation requires either baseUrl or command";
    }

    private static HttpContent BuildRequestContent(JsonElement root)
    {
        var json = JsonSerializer.Serialize(root);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static List<string> BuildCliArgs(SkillOperation op, JsonElement root)
    {
        var args = new List<string>();

        // Add base argv
        if (op.Invoke?.Argv != null)
            args.AddRange(op.Invoke.Argv);

        // Add flags from parameters
        if (op.Invoke?.Flags != null)
        {
            foreach (var (paramName, flagName) in op.Invoke.Flags)
            {
                if (root.TryGetProperty(paramName, out var value))
                {
                    var strValue = value.GetString();
                    if (!string.IsNullOrWhiteSpace(strValue))
                    {
                        args.Add(flagName);
                        args.Add(strValue);
                    }
                }
            }
        }

        return args;
    }

    private async Task<string> ExecuteCommand(string command, List<string> args, CancellationToken ct)
    {
        const int MaxOutputSize = 1024 * 1024; // 1 MiB cap per stream

        var psi = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Use ArgumentList to avoid shell injection (.NET 6+)
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {command}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeout = _skillDef.Runtime?.TimeoutSeconds ?? 30;
        cts.CancelAfter(TimeSpan.FromSeconds(timeout));

        try
        {
            // Read stdout and stderr concurrently to avoid deadlock
            var stdoutTask = ReadStreamAsync(process.StandardOutput, MaxOutputSize, cts.Token);
            var stderrTask = ReadStreamAsync(process.StandardError, MaxOutputSize, cts.Token);

            await process.WaitForExitAsync(cts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
                return $"Command failed with exit code {process.ExitCode}: {stderr}";

            return stdout;
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            return "Command execution timed out";
        }
    }

    /// <summary>
    /// Read from a stream with size cap to avoid memory exhaustion.
    /// </summary>
    private static async Task<string> ReadStreamAsync(StreamReader reader, int maxSize, CancellationToken ct)
    {
        var buffer = new char[Math.Min(4096, maxSize)];
        var result = new StringBuilder(Math.Min(maxSize, 8192));
        int charsRead;

        try
        {
            while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                if (result.Length + charsRead > maxSize)
                {
                    result.Append(new string(buffer, 0, Math.Min(charsRead, maxSize - result.Length)));
                    result.Append("\n[OUTPUT TRUNCATED]");
                    break;
                }

                result.Append(buffer, 0, charsRead);

                // Check for cancellation
                if (ct.IsCancellationRequested)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout or cancellation
        }

        return result.ToString();
    }

    private string BuildParametersSchema()
    {
        if (_skillDef.Operations.Count == 0)
            return """
                {
                  "type": "object",
                  "properties": {
                    "operation": { "type": "string", "description": "Operation to perform" }
                  },
                  "required": ["operation"]
                }
                """;

        var operationEnum = _skillDef.Operations.Select(o => o.Id).ToList();
        var enumJson = string.Join(", ", operationEnum.Select(o => $"\"{o}\""));
        var operationDesc = string.Join(", ", operationEnum);

        return $$"""
            {
              "type": "object",
              "properties": {
                "operation": {
                  "type": "string",
                  "enum": [{{enumJson}}],
                  "description": "Operation to perform: {{operationDesc}}"
                }
              },
              "required": ["operation"]
            }
            """;
    }

    /// <summary>
    /// Build a JSON Schema for a single operation's parameters (excludes the 'operation' routing field).
    /// Falls back to an empty-properties object schema if the operation has no parameters.
    /// </summary>
    private static string BuildOperationParametersSchema(SkillOperation op)
    {
        if (op.Parameters == null || op.Parameters.Count == 0)
            return """{"type":"object","properties":{},"additionalProperties":false}""";

        try
        {
            return JsonSerializer.Serialize(op.Parameters);
        }
        catch
        {
            return """{"type":"object","properties":{},"additionalProperties":false}""";
        }
    }

    private ToolResult FailResult(Stopwatch sw, string error)
    {
        return new ToolResult
        {
            ToolName = Name,
            Success = false,
            Error = error,
            Duration = sw.Elapsed
        };
    }
}
