using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Plugins.BuiltIn.Skills;

public static class DynamicSkillTool
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

/// <summary>
    /// Creates a <see cref="ToolDefinition"/> for a given skill operation.
    /// </summary>
    /// <param name="skill">The skill specification.</param>
    /// <param name="operation">The specific operation within the skill.</param>
    /// <param name="httpClientFactory">The factory to create an HTTP client.</param>
    /// <returns>A new <see cref="ToolDefinition"/>.</returns>
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
        var body = BuildRequestBody(args, operation.Invoke.Flags, queryParams, method);

        var url = ResolveUrl(baseUrl, httpPath, args);

        if (queryParams.Count > 0)
            url += "?" + string.Join("&", queryParams);

        var client = httpClientFactory.CreateClient("SkillHttp");
        using var request = new HttpRequestMessage(new HttpMethod(httpMethod), url);

        var authError = ApplyHttpAuth(request, skill.Runtime.Auth);
        if (!string.IsNullOrWhiteSpace(authError))
        {
            return Failed(operation.Id, authError);
        }

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

    private static object? BuildRequestBody(
        IDictionary<string, object?> args,
        IReadOnlyDictionary<string, string> flags,
        List<string> queryParams,
        string method)
    {
        Dictionary<string, object?>? body = null;

        foreach (var kvp in args)
        {
            if (kvp.Value is null) continue;

            if (!flags.TryGetValue(kvp.Key, out var flagName))
            {
                continue;
            }

            if (method is "GET" or "DELETE")
            {
                queryParams.Add($"{Uri.EscapeDataString(flagName)}={Uri.EscapeDataString(SerializeScalarValue(kvp.Value))}");
            }
            else
            {
                body ??= new Dictionary<string, object?>();
                body[flagName] = kvp.Value;
            }
        }

        return body;
    }

    private static string ResolveUrl(string baseUrl, string httpPath, IDictionary<string, object?> args)
    {
        var url = baseUrl + httpPath;

        if (!url.Contains("{") || !url.Contains("}"))
        {
            return url;
        }

        foreach (var kvp in args)
        {
            if (kvp.Value is null) continue;
            var placeholder = "{" + kvp.Key + "}";
            if (url.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                url = url.Replace(placeholder, Uri.EscapeDataString(SerializeScalarValue(kvp.Value)));
            }
        }

        return url;
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

        AddFlagArguments(psi, args, operation.Invoke.Flags);

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

    private static void AddFlagArguments(
        System.Diagnostics.ProcessStartInfo psi,
        IDictionary<string, object?> args,
        IReadOnlyDictionary<string, string> flags)
    {
        foreach (var kvp in args)
        {
            if (kvp.Value is null) continue;
            if (!flags.TryGetValue(kvp.Key, out var flagName)) continue;

            if (TryGetBooleanLikeValue(kvp.Value, out var boolValue))
            {
                if (boolValue)
                {
                    psi.ArgumentList.Add(flagName);
                }

                continue;
            }

            psi.ArgumentList.Add(flagName);
            psi.ArgumentList.Add(SerializeScalarValue(kvp.Value));
        }
    }

    private static List<ToolParameter>? BuildParameters(SkillOperation operation)
    {
        if (operation.ParametersRaw is null)
            return null;

        if (!operation.ParametersRaw.TryGetValue("properties", out var propsObj) || propsObj is not Dictionary<object, object?> props)
            return null;

        var required = ParseRequiredFields(operation.ParametersRaw);

        var parameters = new List<ToolParameter>();
        foreach (var prop in props)
        {
            var name = prop.Key?.ToString() ?? string.Empty;

            var (type, description) = ParsePropertyMetadata(prop.Value);

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

    private static HashSet<string> ParseRequiredFields(Dictionary<string, object?> parametersRaw)
    {
        var required = new HashSet<string>();
        if (parametersRaw.TryGetValue("required", out var requiredObj) && requiredObj is List<object> requiredList)
        {
            foreach (var r in requiredList)
            {
                if (r is string s)
                    required.Add(s);
            }
        }

        return required;
    }

    private static (string Type, string Description) ParsePropertyMetadata(object? propValue)
    {
        string type = "string", description = string.Empty;
        if (propValue is Dictionary<object, object?> propDict)
        {
            if (propDict.TryGetValue("type", out var typeVal) && typeVal is string ts)
                type = ts;
            if (propDict.TryGetValue("description", out var descVal) && descVal is string ds)
                description = ds;
        }

        return (type, description);
    }

    private static string SerializeScalarValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (TryGetBooleanLikeValue(value, out var boolValue))
        {
            return boolValue ? "true" : "false";
        }

        return value switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonElement element => element.GetRawText(),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static bool TryGetBooleanLikeValue(object? value, out bool result)
    {
        switch (value)
        {
            case bool boolValue:
                result = boolValue;
                return true;
            case JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                result = element.GetBoolean();
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                return TryParseBooleanString(element.GetString(), out result);
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var longValue):
                if (longValue is 0 or 1)
                {
                    result = longValue == 1;
                    return true;
                }

                break;
            case int intValue when intValue is 0 or 1:
                result = intValue == 1;
                return true;
            case long longValue when longValue is 0 or 1:
                result = longValue == 1;
                return true;
            case short shortValue when shortValue is 0 or 1:
                result = shortValue == 1;
                return true;
            case byte byteValue when byteValue is 0 or 1:
                result = byteValue == 1;
                return true;
            case sbyte sbyteValue when sbyteValue is 0 or 1:
                result = sbyteValue == 1;
                return true;
            case uint uintValue when uintValue is 0 or 1:
                result = uintValue == 1;
                return true;
            case ulong ulongValue when ulongValue is 0 or 1:
                result = ulongValue == 1;
                return true;
            case ushort ushortValue when ushortValue is 0 or 1:
                result = ushortValue == 1;
                return true;
            case string text:
                return TryParseBooleanString(text, out result);
        }

        result = default;
        return false;
    }

    private static string? ApplyHttpAuth(HttpRequestMessage request, SkillAuthConfig auth)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(auth);

        if (string.IsNullOrWhiteSpace(auth.Type)
            || auth.Type.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!auth.Type.Equals("bearer", StringComparison.OrdinalIgnoreCase))
        {
            return $"Unsupported HTTP auth type '{auth.Type}'.";
        }

        if (string.IsNullOrWhiteSpace(auth.SecretRef))
        {
            return "HTTP skill bearer auth requires runtime.auth.secretRef.";
        }

        var token = ResolveSecret(auth.SecretRef);
        if (string.IsNullOrWhiteSpace(token))
        {
            return $"HTTP skill bearer auth secret '{auth.SecretRef}' is missing or empty.";
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return null;
    }

    private static string? ResolveSecret(string secretRef)
    {
        var secretPath = Path.Combine("/run/secrets", secretRef);
        if (File.Exists(secretPath))
        {
            var value = File.ReadAllText(secretPath).Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        var directValue = Environment.GetEnvironmentVariable(secretRef);
        if (!string.IsNullOrWhiteSpace(directValue))
        {
            return directValue.Trim();
        }

        var normalizedKey = NormalizeSecretEnvKey(secretRef);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return null;
        }

        var normalizedValue = Environment.GetEnvironmentVariable(normalizedKey);
        return string.IsNullOrWhiteSpace(normalizedValue) ? null : normalizedValue.Trim();
    }

    private static string NormalizeSecretEnvKey(string secretRef)
    {
        var chars = secretRef
            .Trim()
            .ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        return new string(chars);
    }

    private static bool TryParseBooleanString(string? text, out bool result)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            result = default;
            return false;
        }

        var normalized = text.Trim();
        if (bool.TryParse(normalized, out result))
        {
            return true;
        }

        if (normalized == "1" || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) || normalized.Equals("y", StringComparison.OrdinalIgnoreCase) || normalized.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (normalized == "0" || normalized.Equals("no", StringComparison.OrdinalIgnoreCase) || normalized.Equals("n", StringComparison.OrdinalIgnoreCase) || normalized.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = default;
        return false;
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
