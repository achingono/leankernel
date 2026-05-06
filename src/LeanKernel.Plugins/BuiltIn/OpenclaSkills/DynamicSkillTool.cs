using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Plugins.BuiltIn.OpenclaSkills;

/// <summary>
/// Dynamic skill tool that wraps any loaded skill definition.
/// Invokes skills via HTTP, CLI, or composite methods at runtime.
/// No compilation required — just add a SKILL.md file to the skill directory.
/// </summary>
public sealed class DynamicSkillTool : ITool
{
    private readonly SkillDefinition _skillDef;
    private readonly HttpClient _httpClient;
    private readonly ILogger<DynamicSkillTool> _logger;

    public string Name => _skillDef.Name;
    public string Description => _skillDef.Description;
    public string ParametersSchema => _skillDef.ParametersSchema ?? """
        {
          "type": "object",
          "properties": {
            "operation": { "type": "string", "description": "Operation to perform" }
          },
          "required": ["operation"]
        }
        """;

    public DynamicSkillTool(
        SkillDefinition skillDef,
        HttpClient httpClient,
        ILogger<DynamicSkillTool> logger)
    {
        _skillDef = skillDef;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            var root = doc.RootElement;
            var operation = root.GetProperty("operation").GetString() ?? "";

            if (!_skillDef.Operations.TryGetValue(operation, out var op))
                return FailResult(sw, $"Unknown operation '{operation}'. Available: {string.Join(", ", _skillDef.Operations.Keys)}");

            var result = _skillDef.OperationType switch
            {
                "http" => await ExecuteHttpOperation(op, root, ct),
                "cli" => await ExecuteCliOperation(op, root, ct),
                "composite" => await ExecuteCompositeOperation(op, root, ct),
                _ => $"Unsupported operation type: {_skillDef.OperationType}"
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
        if (string.IsNullOrWhiteSpace(_skillDef.BaseUrl) || string.IsNullOrWhiteSpace(op.Endpoint))
            return "HTTP operation requires BaseUrl and Endpoint";

        var url = _skillDef.BaseUrl.TrimEnd('/') + "/" + op.Endpoint.TrimStart('/');

        try
        {
            HttpResponseMessage response = op.HttpMethod switch
            {
                "GET" => await _httpClient.GetAsync(url, ct),
                "POST" => await _httpClient.PostAsync(url, BuildRequestContent(root), ct),
                "PATCH" => await _httpClient.PatchAsync(url, BuildRequestContent(root), ct),
                "DELETE" => await _httpClient.DeleteAsync(url, ct),
                _ => throw new NotSupportedException($"HTTP method {op.HttpMethod} not supported")
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
        if (string.IsNullOrWhiteSpace(_skillDef.CliCommand))
            return "CLI operation requires CliCommand";

        var args = BuildCliArgs(op, root);
        return await ExecuteCommand(_skillDef.CliCommand, args, ct);
    }

    private async Task<string> ExecuteCompositeOperation(SkillOperation op, JsonElement root, CancellationToken ct)
    {
        // Try HTTP first, then fall back to CLI
        if (!string.IsNullOrWhiteSpace(_skillDef.BaseUrl))
        {
            try
            {
                return await ExecuteHttpOperation(op, root, ct);
            }
            catch { /* fall through to CLI */ }
        }

        if (!string.IsNullOrWhiteSpace(_skillDef.CliCommand))
        {
            return await ExecuteCliOperation(op, root, ct);
        }

        return "Composite operation requires either BaseUrl or CliCommand";
    }

    private static HttpContent BuildRequestContent(JsonElement root)
    {
        var json = JsonSerializer.Serialize(root);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static string BuildCliArgs(SkillOperation op, JsonElement root)
    {
        var args = new StringBuilder();

        // Add operation name if present
        if (!string.IsNullOrWhiteSpace(op.Endpoint))
            args.Append(op.Endpoint).Append(" ");

        // Add parameters from JSON
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name != "operation")
            {
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    args.Append($"--{property.Name} \"{value}\" ");
            }
        }

        return args.ToString().Trim();
    }

    private async Task<string> ExecuteCommand(string command, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {command}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(cts.Token);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                return $"Command failed with exit code {process.ExitCode}: {error}";
            }

            return await process.StandardOutput.ReadToEndAsync();
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            return "Command execution timed out";
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
