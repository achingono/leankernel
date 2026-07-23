using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.AspNetCore.Mvc;

namespace LeanKernel.Gateway.Requests;

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
            [FromServices] ILogger<HttpContext> logger) =>
            await HandleChatCompletionsRequestAsync(routePath, context, httpClientFactory, logger));

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
    /// <returns>The proxied result from the internal MAF handler.</returns>
    public static async Task<IResult> HandleChatCompletionsRequestAsync(
        string internalPath,
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<HttpContext> logger)
    {
        using var reader = new StreamReader(context.Request.Body);
        var rawJson = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Results.BadRequest("Empty payload.");
        }

        try
        {
            var rewrittenJson = ReconstructMessage(rawJson);

            var client = httpClientFactory.CreateClient();
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:8080{internalPath}")
            {
                Content = new StringContent(rewrittenJson, System.Text.Encoding.UTF8, Constants.ContentTypes.Json),
            };

            foreach (var header in context.Request.Headers)
            {
                if (!header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                {
                    requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }

            var responseMessage = await client.SendAsync(requestMessage);
            var responseStream = await responseMessage.Content.ReadAsStreamAsync();
            var contentType = responseMessage.Content.Headers.ContentType?.ToString() ?? Constants.ContentTypes.Json;

            if (responseMessage.IsSuccessStatusCode)
            {
                return Results.Stream(responseStream, contentType);
            }

            using var errorReader = new StreamReader(responseStream);
            var errorBody = await errorReader.ReadToEndAsync();
            logger.LogWarning("Chat completions proxy returned {StatusCode}: {Body}", (int)responseMessage.StatusCode, errorBody);
            return Results.Content(errorBody, contentType, statusCode: (int)responseMessage.StatusCode);
        }
        catch (JsonException)
        {
            return Results.BadRequest("Invalid JSON format sent by client.");
        }
    }

    /// <summary>
    /// Re-orders each chat message object so role appears before content.
    /// </summary>
    /// <param name="rawJson">Original request payload.</param>
    /// <returns>Rewritten payload with role-first message objects.</returns>
    internal static string ReconstructMessage(string rawJson)
    {
        var rootNode = JsonNode.Parse(rawJson);

        if (rootNode?["messages"] is JsonArray messagesArray)
        {
            var serializedMessages = new JsonArray();

            foreach (var message in messagesArray)
            {
                if (message is JsonObject msgObj)
                {
                    var roleVal = msgObj["role"]?.ToString() ?? "user";
                    var contentVal = msgObj["content"]?.ToString() ?? string.Empty;

                    var compliantMessageObj = new JsonObject
                    {
                        { "role", roleVal },
                        { "content", contentVal },
                    };

                    foreach (var property in msgObj)
                    {
                        if (property.Key != "role" && property.Key != "content")
                        {
                            compliantMessageObj.Add(property.Key, property.Value?.DeepClone());
                        }
                    }

                    serializedMessages.Add(compliantMessageObj);
                }
            }

            rootNode["messages"] = serializedMessages;
        }

        return rootNode!.ToJsonString();
    }
}
