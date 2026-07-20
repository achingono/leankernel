using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.Dynamic;

/// <summary>
/// Converts a <see cref="SkillOperation"/> into a LeanKernel <see cref="ToolDefinition"/>
/// that executes HTTP calls with egress validation and secret resolution per Appendix A.
/// </summary>
public static class DynamicSkillTool
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates a tool definition from a skill definition and one of its operations.
    /// The tool name is {skill.Name}_{operation.Id}.
    /// </summary>
    /// <param name="skill">The skill definition.</param>
    /// <param name="operation">The operation to bind.</param>
    /// <param name="scopeFactory">The service scope factory for creating scoped dependencies.</param>
    /// <returns>A <see cref="ToolDefinition"/> for the given skill operation.</returns>
    public static ToolDefinition Create(
        SkillDefinition skill,
        SkillOperation operation,
        IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(scopeFactory);

        var toolName = $"{skill.Name}_{operation.Id}";

        return new ToolDefinition
        {
            Name = toolName,
            Description = $"{skill.Description}: {operation.Summary}",
            Category = skill.Category ?? "dynamic",
            Parameters = operation.Parameters
                .Select(p => new ToolParameter
                {
                    Name = p.Name,
                    Type = p.Type,
                    Description = p.Description,
                    Required = p.Required
                })
                .ToList(),
            Handler = async (args, ct) =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var globalSettings = scope.ServiceProvider
                        .GetRequiredService<IOptions<AgentSettings>>().Value
                        .Tools.DynamicHttp;

                    // Validate and build the target URL
                    var url = BuildUrl(skill, operation, args, out var urlError);
                    if (urlError is not null)
                    {
                        return new ToolResult { ToolName = toolName, Success = false, Error = urlError };
                    }

                    // Egress validation
                    var egressError = EgressValidator.TryValidateEgressTarget(
                        url!, skill.AllowedHosts, globalSettings.AllowHosts);
                    if (egressError is not null)
                    {
                        return new ToolResult { ToolName = toolName, Success = false, Error = egressError };
                    }

                    // Resolve bearer token if needed
                    string? bearerToken = null;
                    if (skill.Runtime.Auth.Type == "bearer")
                    {
                        bearerToken = ResolveSecret(skill.Runtime.Auth.SecretRef, out var secretError);
                        if (secretError is not null)
                        {
                            return new ToolResult { ToolName = toolName, Success = false, Error = secretError };
                        }
                    }

                    var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                    var client = factory.CreateClient("dynamic-skill");
                    client.Timeout = TimeSpan.FromSeconds(skill.Runtime.TimeoutSeconds);

                    var response = await ExecuteRequestAsync(
                        client, operation, url!, args, bearerToken, ct)
                        .ConfigureAwait(false);

                    return new ToolResult
                    {
                        ToolName = toolName,
                        Success = response.IsSuccessStatusCode,
                        Output = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false),
                        Error = response.IsSuccessStatusCode ? null
                            : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                    };
                }
                catch (Exception ex)
                {
                    return new ToolResult { ToolName = toolName, Success = false, Error = ex.Message };
                }
            }
        };
    }

    private static string? BuildUrl(
        SkillDefinition skill,
        SkillOperation operation,
        IReadOnlyDictionary<string, object?> args,
        out string? error)
    {
        error = null;
        var baseUrl = skill.Runtime.BaseUrl.TrimEnd('/');
        var path = operation.HttpPath;

        // Substitute {placeholder} segments from args
        foreach (var param in operation.Parameters.Where(p =>
            path.Contains($"{{{p.Name}}}", StringComparison.OrdinalIgnoreCase)))
        {
            var val = ToolArgumentReader.GetString(args, param.Name);
            if (val is null && param.Required)
            {
                error = $"Required parameter '{param.Name}' is missing.";
                return null;
            }

            path = path.Replace(
                $"{{{param.Name}}}",
                Uri.EscapeDataString(val ?? string.Empty),
                StringComparison.OrdinalIgnoreCase);
        }

        return $"{baseUrl}{path}";
    }

    private static async Task<HttpResponseMessage> ExecuteRequestAsync(
        HttpClient client,
        SkillOperation operation,
        string url,
        IReadOnlyDictionary<string, object?> args,
        string? bearerToken,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            new HttpMethod(operation.HttpMethod), url);

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        // Parameters not consumed as path placeholders become query or body
        var remainingParams = operation.Parameters
            .Where(p => !operation.HttpPath.Contains($"{{{p.Name}}}", StringComparison.OrdinalIgnoreCase))
            .Where(p => args.ContainsKey(p.Name))
            .ToDictionary(p => p.Name, p => ToolArgumentReader.GetString(args, p.Name));

        if (operation.HttpMethod is "POST" or "PUT" or "PATCH")
        {
            var body = JsonSerializer.Serialize(remainingParams, JsonOptions);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        else if (remainingParams.Count > 0)
        {
            var query = string.Join("&", remainingParams
                .Where(kv => kv.Value is not null)
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

            var sep = url.Contains('?') ? '&' : '?';
            request.RequestUri = new Uri($"{url}{sep}{query}");
        }

        return await client.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static string? ResolveSecret(string? secretRef, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(secretRef))
        {
            error = "auth.secretRef is required for bearer authentication but is not set.";
            return null;
        }

        // Try /run/secrets/<ref> first
        var filePath = $"/run/secrets/{secretRef}";
        if (File.Exists(filePath))
        {
            return File.ReadAllText(filePath).Trim();
        }

        // Try SKILL__<REF_UPPER> env var
        var envVar = $"SKILL__{secretRef.ToUpperInvariant().Replace('-', '_')}";
        var envVal = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(envVal))
        {
            return envVal;
        }

        error = $"Secret '{secretRef}' not found in /run/secrets/{secretRef} or environment variable {envVar}.";
        return null;
    }
}