using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Plugins.BuiltIn.Skills;

public static class DynamicSkillTool
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ToolDefinition CreateTool(
        SkillDefinition skill,
        SkillOperation operation,
        IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        var name = $"{skill.Name}_{operation.Id}";
        var parameters = BuildParameters(operation);

        return new ToolDefinition
        {
            Name = name,
            Description = $"{skill.Description} — {operation.Summary}",
            Category = skill.Metadata.TryGetValue("category", out var cat) ? cat?.ToString() : null,
            Parameters = parameters,
            Handler = async (args, ct) =>
            {
                try
                {
                    if (skill.Runtime.Type == "http")
                    {
                        return await ExecuteHttpAsync(skill, operation, args, httpClientFactory, ct);
                    }

                    return await ExecuteCliAsync(skill, operation, args, ct);
                }
                catch (OperationCanceledException)
                {
                    return new ToolResult
                    {
                        ToolName = name,
                        Success = false,
                        Error = "Skill execution was cancelled"
                    };
                }
                catch (Exception ex)
                {
                    return new ToolResult
                    {
                        ToolName = name,
                        Success = false,
                        Error = $"Skill execution failed: {ex.Message}"
                    };
                }
            }
        };
    }

    private static async Task<ToolResult> ExecuteHttpAsync(
        SkillDefinition skill,
        SkillOperation operation,
        IDictionary<string, object?> args,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var baseUrl = skill.Runtime.BaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Failed(operation.Id, "HTTP skill has no baseUrl configured");
        }

        var httpPath = operation.Invoke.HttpPath ?? string.Empty;
        var httpMethod = operation.Invoke.HttpMethod ?? "GET";
        var method = httpMethod.ToUpperInvariant();

        var queryParams = new List<string>();
        var flags = operation.Invoke.Flags;
        object? body = null;

        foreach (var kvp in args)
        {
            if (kvp.Value is null) continue;

            if (flags.TryGetValue(kvp.Key, out var flagName))
            {
                if (method is "GET" or "DELETE")
                {
                    queryParams.Add($"{Uri.EscapeDataString(flagName)}={Uri.EscapeDataString(kvp.Value.ToString()!)}");
                }
                else
                {
                    if (body is null)
                        body = new Dictionary<string, object?>();
                    ((Dictionary<string, object?>)body)[flagName] = kvp.Value;
                }
            }
        }

        var url = baseUrl + httpPath;

        if (url.Contains("{") && url.Contains("}"))
        {
            foreach (var kvp in args)
            {
                if (kvp.Value is null) continue;
                var placeholder = "{" + kvp.Key + "}";
                if (url.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                {
                    url = url.Replace(placeholder, Uri.EscapeDataString(kvp.Value.ToString()!));
                }
            }
        }

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        var client = httpClientFactory.CreateClient("SkillHttp");
        using var request = new HttpRequestMessage(new HttpMethod(httpMethod), url);

        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(body ?? new { }, options: JsonOptions);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return new ToolResult
            {
                ToolName = operation.Id,
                Success = false,
                Error = $"HTTP {(int)response.StatusCode}: {responseBody}"
            };
        }

        return new ToolResult
        {
            ToolName = operation.Id,
            Success = true,
            Output = Truncate(responseBody)
        };
    }

    private static Task<ToolResult> ExecuteCliAsync(
        SkillDefinition skill,
        SkillOperation operation,
        IDictionary<string, object?> args,
        CancellationToken ct)
    {
        var command = skill.Runtime.Command;
        if (string.IsNullOrWhiteSpace(command))
        {
            return Task.FromResult(Failed(operation.Id, "CLI skill has no command configured"));
        }

        var psi = new System.Diagnostics.ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in operation.Invoke.Argv)
        {
            psi.ArgumentList.Add(arg);
        }

        var flags = operation.Invoke.Flags;
        foreach (var kvp in args)
        {
            if (kvp.Value is null) continue;
            if (flags.TryGetValue(kvp.Key, out var flagName))
            {
                psi.ArgumentList.Add(flagName);
                psi.ArgumentList.Add(kvp.Value.ToString()!);
            }
        }

        var process = new System.Diagnostics.Process { StartInfo = psi };
        process.Start();

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var readTask = Task.WhenAll(
            ReadStreamAsync(process.StandardOutput, stdout, ct),
            ReadStreamAsync(process.StandardError, stderr, ct));

        var completed = process.WaitForExit(skill.Runtime.TimeoutSeconds * 1000);
        readTask.Wait(ct);

        if (!completed)
        {
            try { process.Kill(); } catch { }
            return Task.FromResult(new ToolResult
            {
                ToolName = operation.Id,
                Success = false,
                Error = $"CLI skill timed out after {skill.Runtime.TimeoutSeconds}s"
            });
        }

        if (process.ExitCode != 0)
        {
            return Task.FromResult(new ToolResult
            {
                ToolName = operation.Id,
                Success = false,
                Error = $"Exit code {process.ExitCode}: {Truncate(stderr.ToString())}"
            });
        }

        return Task.FromResult(new ToolResult
        {
            ToolName = operation.Id,
            Success = true,
            Output = Truncate(stdout.ToString())
        });
    }

    private static async Task ReadStreamAsync(StreamReader reader, StringBuilder sb, CancellationToken ct)
    {
        var buffer = new char[4096];
        while (!ct.IsCancellationRequested)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read == 0) break;
            sb.Append(buffer, 0, read);
        }
    }

    private static List<ToolParameter>? BuildParameters(SkillOperation operation)
    {
        if (operation.ParametersRaw is null)
            return null;

        if (!operation.ParametersRaw.TryGetValue("properties", out var propsObj) || propsObj is not Dictionary<object, object?> props)
            return null;

        var required = new HashSet<string>();
        if (operation.ParametersRaw.TryGetValue("required", out var requiredObj) && requiredObj is List<object> requiredList)
        {
            foreach (var r in requiredList)
            {
                if (r is string s)
                    required.Add(s);
            }
        }

        var parameters = new List<ToolParameter>();
        foreach (var prop in props)
        {
            var name = prop.Key?.ToString() ?? string.Empty;
            var propValue = prop.Value;

            var type = "string";
            var description = string.Empty;

            if (propValue is Dictionary<object, object?> propDict)
            {
                if (propDict.TryGetValue("type", out var typeVal) && typeVal is string ts)
                    type = ts;
                if (propDict.TryGetValue("description", out var descVal) && descVal is string ds)
                    description = ds;
            }

            parameters.Add(new ToolParameter
            {
                Name = name,
                Type = type,
                Description = description,
                Required = required.Contains(name)
            });
        }

        return parameters;
    }

    private static ToolResult Failed(string toolName, string error) => new()
    {
        ToolName = toolName,
        Success = false,
        Error = error
    };

    private const int MaxOutputChars = 12_000;

    private static string Truncate(string value)
        => value.Length <= MaxOutputChars ? value : value[..MaxOutputChars];
}
