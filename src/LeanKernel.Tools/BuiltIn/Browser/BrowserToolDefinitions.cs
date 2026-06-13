using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools.BuiltIn.Browser;

/// <summary>
/// Built-in browser automation tool definitions.
/// </summary>
public static class BrowserToolDefinitions
{
    /// <summary>
    /// The activity source name used by browser tool handlers.
    /// </summary>
    public const string ActivitySourceName = "LeanKernel.Tools.Browser";

    private const int MaxTaskBytes = 4096;
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates the browser run submission tool.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>The browser run task tool definition.</returns>
    public static ToolDefinition CreateRunTaskTool(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "browser_run_task",
            Description = "Submit an asynchronous browser automation task. Returns a runId; poll browser_get_run until the run reaches a terminal status.",
            Category = "browser",
            Parameters =
            [
                new ToolParameter { Name = "task", Type = "string", Description = "Natural-language browser task, up to 4096 UTF-8 bytes", Required = true },
                new ToolParameter { Name = "start_url", Type = "string", Description = "Optional absolute HTTP/HTTPS URL where the browser starts", Required = false },
                new ToolParameter { Name = "model", Type = "string", Description = "Optional LiteLLM model alias override", Required = false },
                new ToolParameter { Name = "request_key", Type = "string", Description = "Optional concurrency serialization key", Required = false },
                new ToolParameter { Name = "request_id", Type = "string", Description = "Optional idempotency key", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                using var activity = ActivitySource.StartActivity("browser_run_task");
                var task = ToolArgumentReader.GetString(args, "task");
                if (string.IsNullOrWhiteSpace(task))
                {
                    return Failed("browser_run_task", "Task is required");
                }

                if (Encoding.UTF8.GetByteCount(task) > MaxTaskBytes)
                {
                    return Failed("browser_run_task", $"Task must be no more than {MaxTaskBytes} UTF-8 bytes.");
                }

                var startUrl = NormalizeOptional(ToolArgumentReader.GetString(args, "start_url"));
                if (startUrl is not null && !IsAbsoluteHttpUrl(startUrl))
                {
                    return Failed("browser_run_task", "start_url must be an absolute HTTP or HTTPS URL");
                }

                using var scope = scopeFactory.CreateScope();
                var config = GetBrowserConfig(scope.ServiceProvider);
                var client = scope.ServiceProvider.GetRequiredService<IWebwrightClient>();
                var request = new BrowserRunTaskRequest(
                    task.Trim(),
                    startUrl,
                    NormalizeOptional(ToolArgumentReader.GetString(args, "model")) ?? NormalizeOptional(config.DefaultModel),
                    NormalizeOptional(ToolArgumentReader.GetString(args, "request_key")),
                    NormalizeOptional(ToolArgumentReader.GetString(args, "request_id")));

                return await ExecuteAsync(
                    "browser_run_task",
                    () => client.SubmitRunAsync(request, ct)).ConfigureAwait(false);
            }
        };
    }

    /// <summary>
    /// Creates the browser run polling tool.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>The browser get run tool definition.</returns>
    public static ToolDefinition CreateGetRunTool(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "browser_get_run",
            Description = "Poll a browser automation run by run_id and return status, final datum, errors, and artifact manifest.",
            Category = "browser",
            Parameters =
            [
                new ToolParameter { Name = "run_id", Type = "string", Description = "Browser run identifier returned by browser_run_task", Required = true },
                new ToolParameter { Name = "wait_seconds", Type = "integer", Description = "Reserved for future long polling; rejected in v1", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                using var activity = ActivitySource.StartActivity("browser_get_run");
                if (args.TryGetValue("wait_seconds", out var waitSeconds) && waitSeconds is not null)
                {
                    return Failed("browser_get_run", "wait_seconds is reserved for future long-polling and is not supported in v1.");
                }

                var runId = ToolArgumentReader.GetString(args, "run_id");
                if (string.IsNullOrWhiteSpace(runId))
                {
                    return Failed("browser_get_run", "run_id is required");
                }

                using var scope = scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<IWebwrightClient>();
                return await ExecuteAsync(
                    "browser_get_run",
                    () => client.GetRunAsync(runId.Trim(), ct)).ConfigureAwait(false);
            }
        };
    }

    /// <summary>
    /// Creates the browser artifact retrieval tool.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>The browser get artifact tool definition.</returns>
    public static ToolDefinition CreateGetArtifactTool(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "browser_get_artifact",
            Description = "Fetch one manifest-listed browser artifact by run_id and artifact_id. Output bytes are base64 encoded.",
            Category = "browser",
            Parameters =
            [
                new ToolParameter { Name = "run_id", Type = "string", Description = "Browser run identifier", Required = true },
                new ToolParameter { Name = "artifact_id", Type = "string", Description = "Opaque artifact id from browser_get_run manifest", Required = true },
                new ToolParameter { Name = "max_bytes", Type = "integer", Description = "Maximum bytes to return; defaults to Webwright:MaxArtifactBytes", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                using var activity = ActivitySource.StartActivity("browser_get_artifact");
                var runId = ToolArgumentReader.GetString(args, "run_id");
                var artifactId = ToolArgumentReader.GetString(args, "artifact_id");
                if (string.IsNullOrWhiteSpace(runId))
                {
                    return Failed("browser_get_artifact", "run_id is required");
                }

                if (string.IsNullOrWhiteSpace(artifactId))
                {
                    return Failed("browser_get_artifact", "artifact_id is required");
                }

                using var scope = scopeFactory.CreateScope();
                var config = GetBrowserConfig(scope.ServiceProvider);
                var requestedBytes = ToolArgumentReader.GetInt32OrDefault(args, "max_bytes", config.MaxArtifactBytes);
                if (requestedBytes <= 0)
                {
                    return Failed("browser_get_artifact", "max_bytes must be positive");
                }

                var maxBytes = Math.Min(requestedBytes, Math.Max(1, config.MaxArtifactBytes));
                var client = scope.ServiceProvider.GetRequiredService<IWebwrightClient>();
                return await ExecuteArtifactAsync(
                    "browser_get_artifact",
                    () => client.GetArtifactAsync(runId.Trim(), artifactId.Trim(), maxBytes, ct)).ConfigureAwait(false);
            }
        };
    }

    /// <summary>
    /// Creates the browser run cancellation tool.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>The browser cancel run tool definition.</returns>
    public static ToolDefinition CreateCancelRunTool(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "browser_cancel_run",
            Description = "Request idempotent cancellation of a browser automation run.",
            Category = "browser",
            Parameters =
            [
                new ToolParameter { Name = "run_id", Type = "string", Description = "Browser run identifier", Required = true }
            ],
            Handler = async (args, ct) =>
            {
                using var activity = ActivitySource.StartActivity("browser_cancel_run");
                var runId = ToolArgumentReader.GetString(args, "run_id");
                if (string.IsNullOrWhiteSpace(runId))
                {
                    return Failed("browser_cancel_run", "run_id is required");
                }

                using var scope = scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<IWebwrightClient>();
                return await ExecuteAsync(
                    "browser_cancel_run",
                    () => client.CancelRunAsync(runId.Trim(), ct)).ConfigureAwait(false);
            }
        };
    }

    private static async Task<ToolResult> ExecuteAsync<T>(string toolName, Func<Task<T>> action)
    {
        try
        {
            var response = await action().ConfigureAwait(false);
            return new ToolResult
            {
                ToolName = toolName,
                Success = true,
                Output = Truncate(JsonSerializer.Serialize(response, JsonOptions))
            };
        }
        catch (WebwrightException ex)
        {
            return Failed(toolName, FormatError(ex));
        }
    }

    private static async Task<ToolResult> ExecuteArtifactAsync(string toolName, Func<Task<BrowserArtifactContent>> action)
    {
        try
        {
            var artifact = await action().ConfigureAwait(false);
            var output = new
            {
                artifact.RunId,
                artifact.ArtifactId,
                artifact.ContentType,
                Base64 = Convert.ToBase64String(artifact.Bytes),
                artifact.Truncated
            };

            return new ToolResult
            {
                ToolName = toolName,
                Success = true,
                Output = Truncate(JsonSerializer.Serialize(output, JsonOptions))
            };
        }
        catch (WebwrightException ex)
        {
            return Failed(toolName, FormatError(ex));
        }
    }

    private static ToolResult Failed(string toolName, string error) => new()
    {
        ToolName = toolName,
        Success = false,
        Error = error
    };

    private static string FormatError(WebwrightException ex)
    {
        var payload = new WebwrightError(ex.Code, ex.Message, ex.Details);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static WebwrightConfig GetBrowserConfig(IServiceProvider serviceProvider)
        => serviceProvider.GetService<IOptions<LeanKernelConfig>>()?.Value.Webwright ?? new WebwrightConfig();

    private static string? NormalizeOptional(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsAbsoluteHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string Truncate(string value)
    {
        const int fallbackMaxChars = 12_000;
        return value.Length <= fallbackMaxChars ? value : value[..fallbackMaxChars];
    }
}
