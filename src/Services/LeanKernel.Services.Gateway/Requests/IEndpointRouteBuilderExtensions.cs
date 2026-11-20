using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

using LeanKernel.Services.Gateway.Configuration;

using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LeanKernel.Services.Gateway.Requests;

/// <summary>
/// Extension methods that expose a proxied OpenAI Chat Completions endpoint.
/// </summary>
public static class IEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Registers the public chat-completions proxy endpoint and the internal MAF handler.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="agentName">The keyed AI agent name to resolve per request.</param>
    /// <param name="internalPath">The internal MAF handler path.</param>
    /// <param name="mapOptions">Optional mapping options.</param>
    /// <returns>The mapped internal endpoint builder.</returns>
    public static IEndpointConventionBuilder MapProxiedOpenAIChatCompletions(
        this IEndpointRouteBuilder endpoints,
        string agentName,
        string? internalPath,
        OpenAIChatCompletionsMapOptions? mapOptions = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        var routePath = internalPath ?? "/v1/internal/completions";
        var httpContextAccessor = endpoints.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var scopedAgent = new ScopedKeyedAIAgentProxy(agentName, httpContextAccessor);

        endpoints.MapPost("/v1/chat/completions", async (
            HttpContext context,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] ILogger<HttpContext> logger,
            [FromServices] IOptions<SessionOptions> sessionOptions,
            [FromServices] IOptions<HardeningSettings> hardeningOptions) =>
            await HandleChatCompletionsRequestAsync(routePath, context, httpClientFactory, logger, sessionOptions, hardeningOptions));

        var effectiveMapOptions = mapOptions ?? new OpenAIChatCompletionsMapOptions
        {
            RunOptionsFactory = _ => null,
        };

        return endpoints.MapOpenAIChatCompletions(
            scopedAgent,
            routePath,
            effectiveMapOptions);
    }

    /// <summary>
    /// Registers the OpenAI-compatible model discovery endpoint.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="agentName">The configured agent name to expose as the single model.</param>
    /// <returns>The mapped endpoint builder.</returns>
    public static IEndpointConventionBuilder MapOpenAIModels(
        this IEndpointRouteBuilder endpoints,
        string agentName)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        return endpoints.MapGet("/v1/models", () => Results.Json(new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    id = agentName,
                    @object = "model",
                    created = 0,
                    owned_by = agentName,
                },
            },
        }));
    }

    /// <summary>
    /// Rewrites and forwards chat-completions requests to the internal MAF handler.
    /// </summary>
    /// <param name="internalPath">The internal MAF route receiving rewritten payloads.</param>
    /// <param name="context">The active HTTP request context.</param>
    /// <param name="httpClientFactory">The factory used to create the loopback HTTP client.</param>
    /// <param name="logger">The logger used for proxy diagnostics.</param>
    /// <param name="sessionOptions">The configured session cookie.</param>
    /// <param name="hardeningOptions">The configured correlation header.</param>
    /// <returns>The proxied result from the internal MAF handler.</returns>
    public static async Task<IResult> HandleChatCompletionsRequestAsync(
        string internalPath,
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<HttpContext> logger,
        IOptions<SessionOptions>? sessionOptions = null,
        IOptions<HardeningSettings>? hardeningOptions = null)
    {
        using var reader = new StreamReader(context.Request.Body);
        var rawJson = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Results.BadRequest("Empty payload.");
        }

        try
        {
            context.RequestAborted.ThrowIfCancellationRequested();
            var rewrittenJson = ReconstructMessage(rawJson, out var invalidTool);
            if (invalidTool is { } invalid)
            {
                return InvalidToolResult(invalid.Parameter, invalid.Message);
            }

            var localPort = context.Connection.LocalPort;
            if (localPort <= 0)
            {
                throw new HttpRequestException("The local gateway listener port is unavailable.");
            }

            var transport = context.Features.Get<ITlsConnectionFeature>() is null ? "http" : "https";
            var selfUrl = $"{transport}://localhost:{localPort}{internalPath}";
            var client = httpClientFactory.CreateClient(Constants.HttpClientNames.ChatCompletionsProxy);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, selfUrl)
            {
                Content = new StringContent(rewrittenJson, System.Text.Encoding.UTF8, Constants.ContentTypes.Json),
            };

            requestMessage.Headers.Host = context.Request.Host.Value;
            if (context.Request.Headers.TryGetValue("Authorization", out var authorization))
            {
                requestMessage.Headers.TryAddWithoutValidation("Authorization", authorization.ToArray());
            }

            var cookieName = sessionOptions?.Value.Cookie.Name ?? ".AspNetCore.Session";
            if (context.Request.Cookies.TryGetValue(cookieName, out var sessionCookie))
            {
                requestMessage.Headers.TryAddWithoutValidation("Cookie", $"{cookieName}={sessionCookie}");
            }

            var correlationHeader = hardeningOptions?.Value.CorrelationIdHeader ?? "X-Correlation-ID";
            if (!string.Equals(correlationHeader, "Authorization", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(correlationHeader, "Cookie", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(correlationHeader, "Host", StringComparison.OrdinalIgnoreCase))
            {
                requestMessage.Headers.TryAddWithoutValidation(correlationHeader, context.TraceIdentifier);
            }

            var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                var responseMessage = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                return new SseToolCallFilteringResult(responseMessage, timeout, internalPath, logger);
            }
            catch
            {
                timeout.Dispose();
                throw;
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Chat completions proxy aborted by the caller before the internal agent handler completed.");
            throw;
        }
        catch (OperationCanceledException ex)
        {
            return MapProxyFailure(internalPath, ex, logger);
        }
        catch (HttpRequestException ex)
        {
            return MapProxyFailure(internalPath, ex, logger);
        }
        catch (JsonException)
        {
            return Results.BadRequest("Invalid JSON format sent by client.");
        }
        catch (Exception ex)
        {
            return MapProxyFailure(internalPath, ex, logger);
        }
    }

    /// <summary>
    /// Maps a proxy failure to a helpful OpenAI-compatible error result.
    /// </summary>
    /// <param name="internalPath">The internal MAF route that was being awaited.</param>
    /// <param name="exception">The exception thrown while proxying.</param>
    /// <param name="logger">The logger used for proxy diagnostics.</param>
    /// <returns>The mapped error result.</returns>
    internal static IResult MapProxyFailure(
        string internalPath,
        Exception exception,
        ILogger<HttpContext> logger)
    {
        switch (exception)
        {
            case HttpRequestException:
                logger.LogError(exception, "Chat completions proxy could not reach the internal agent route at {InternalPath}.", internalPath);
                return OpenAiErrorResult(
                    StatusCodes.Status502BadGateway,
                    "The agent runtime could not be reached. Please try again.");

            case OperationCanceledException:
                logger.LogWarning(exception, "Chat completions proxy timed out while awaiting the internal agent route at {InternalPath}.", internalPath);
                return OpenAiErrorResult(
                    StatusCodes.Status504GatewayTimeout,
                    "The chat request timed out while processing. Please try again.");

            default:
                logger.LogError(exception, "Unexpected error while proxying chat completions through {InternalPath}.", internalPath);
                return OpenAiErrorResult(
                    StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred while processing the chat request.");
        }
    }

    /// <summary>
    /// Produces an OpenAI-compatible error result so standard OpenAI clients can parse the failure.
    /// </summary>
    /// <param name="statusCode">The HTTP status code to return.</param>
    /// <param name="message">The user-facing error message.</param>
    /// <returns>The JSON error result.</returns>
    private static IResult OpenAiErrorResult(int statusCode, string message)
        => Results.Json(
            new
            {
                error = new
                {
                    message,
                    type = "server_error",
                    param = (string?)null,
                    code = "server_error",
                },
            },
            statusCode: statusCode);

    private static IResult InvalidToolResult(string parameter, string message)
        => Results.Json(
            new
            {
                error = new
                {
                    message,
                    type = "invalid_request_error",
                    param = parameter,
                    code = "invalid_value",
                },
            },
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Re-orders each chat message object so role appears before content.
    /// </summary>
    /// <param name="rawJson">Original request payload.</param>
    /// <param name="invalidTool">Validation failure for a malformed tool, if present.</param>
    /// <returns>Rewritten payload with role-first message objects.</returns>
    [SuppressMessage("Critical Code Smell", "S3776", Justification = "JSON message reconstruction normalizes property order across chat messages.")]
    internal static string ReconstructMessage(string rawJson, out (string Parameter, string Message)? invalidTool)
    {
        var rootNode = JsonNode.Parse(rawJson);
        invalidTool = FindInvalidTool(rootNode);
        if (invalidTool is not null)
        {
            return string.Empty;
        }

        if (rootNode?["messages"] is JsonArray messagesArray)
        {
            var serializedMessages = new JsonArray();

            foreach (var message in messagesArray)
            {
                if (message is JsonObject msgObj)
                {
                    serializedMessages.Add(ReconstructSingleMessage(msgObj));
                }
            }

            rootNode["messages"] = serializedMessages;
        }

        return rootNode!.ToJsonString();
    }

    private static (string Parameter, string Message)? FindInvalidTool(JsonNode? rootNode)
    {
        const string invalidMessage = "Each tool must be an object with a non-empty string type.";
        var toolsNode = rootNode?["tools"];
        if (toolsNode is not null && toolsNode is not JsonArray)
        {
            return ("tools", invalidMessage);
        }

        if (toolsNode is not JsonArray tools)
        {
            return null;
        }

        for (var i = 0; i < tools.Count; i++)
        {
            if (tools[i] is not JsonObject tool)
            {
                return ($"tools[{i}]", invalidMessage);
            }

            if (tool["type"] is not JsonValue type || !type.TryGetValue<string>(out var typeName) || string.IsNullOrWhiteSpace(typeName))
            {
                return ($"tools[{i}].type", invalidMessage);
            }

            // The MAF chat-completions Tool model currently supports these two discriminators.
            if (typeName is not "function" and not "custom")
            {
                return ($"tools[{i}].type", "Tool type must be 'function' or 'custom'.");
            }
        }

        return null;
    }

    private static JsonObject ReconstructSingleMessage(JsonObject msgObj)
    {
        var roleVal = msgObj["role"]?.ToString();
        if (roleVal is null)
        {
            return msgObj;
        }

        var contentNode = msgObj["content"];
        var compliantMessageObj = new JsonObject
        {
            { "role", roleVal },
        };

        if (contentNode is not null)
        {
            compliantMessageObj.Add("content", contentNode.DeepClone());
        }

        foreach (var property in msgObj)
        {
            if (property.Key != "role" && property.Key != "content")
            {
                compliantMessageObj.Add(property.Key, property.Value?.DeepClone());
            }
        }

        return compliantMessageObj;
    }
}
