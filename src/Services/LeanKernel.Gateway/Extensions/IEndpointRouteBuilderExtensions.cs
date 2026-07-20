using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.AspNetCore.Mvc;
namespace LeanKernel.Gateway.Requests;

/// <summary>
/// Extension methods for <see cref="IEndpointRouteBuilder"/> that register a proxied
/// OpenAI Chat Completions endpoint. The proxy re-orders message properties so the
/// <c>role</c> discriminator appears first—required by the MAF <c>ChatCompletionRequestMessage</c>
/// polymorphic deserializer—then forwards the request to an internal MAF handler.
/// </summary>
public static class IEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Registers a <c>POST /v1/chat/completions</c> proxy endpoint that re-orders
    /// message properties and forwards to an internal MAF Chat Completions handler,
    /// along with the internal handler itself at <paramref name="internalPath"/>.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="agentName">The keyed AI agent name to resolve for each request.</param>
    /// <param name="internalPath">
    /// The path for the internal MAF handler (default <c>/v1/internal/completions</c>).
    /// </param>
    /// <param name="mapOptions">
    /// Optional <see cref="OpenAIChatCompletionsMapOptions"/>; defaults to accepting all
    /// client-supplied settings silently (agent uses its own configuration).
    /// </param>
    /// <returns>A convention builder for the registered endpoints.</returns>
    public static IEndpointConventionBuilder MapProxiedOpenAIChatCompletions(this IEndpointRouteBuilder endpoints, string agentName, string? internalPath, OpenAIChatCompletionsMapOptions? mapOptions = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        var routePath = internalPath ?? Constants.Http.InternalCompletionsPath;
        var httpContextAccessor = endpoints.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var scopedAgent = new ScopedKeyedAIAgentProxy(agentName, httpContextAccessor);

        endpoints.MapPost("/v1/chat/completions", async (
            HttpContext context,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] ILogger<HttpContext> logger) =>
            await HandleChatCompletionsRequestAsync(routePath, context, httpClientFactory, logger)
        );

        return endpoints.MapOpenAIChatCompletions(scopedAgent, routePath, mapOptions ?? new OpenAIChatCompletionsMapOptions
        {
            RunOptionsFactory = _ => null
        });
    }

    /// <summary>
    /// Handles an incoming Chat Completions request by reading the JSON payload,
    /// re-ordering message properties so <c>role</c> is first, forwarding to the
    /// internal MAF handler, and returning the response with the original status code.
    /// </summary>
    /// <param name="internalPath">The internal MAF handler path.</param>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <returns>An <see cref="IResult"/> representing the proxied response.</returns>
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
            string rewrittenJson = ReconstructMessage(rawJson);

            var client = httpClientFactory.CreateClient();
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:8080{internalPath}")
            {
                Content = new StringContent(
                    rewrittenJson,
                    System.Text.Encoding.UTF8,
                    Constants.Http.ApplicationJson
                )
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
            var contentType = responseMessage.Content.Headers.ContentType?.ToString() ?? Constants.Http.ApplicationJson;

            if (responseMessage.IsSuccessStatusCode)
            {
                return Results.Stream(responseStream, contentType);
            }

            using var errorReader = new StreamReader(responseStream);
            var errorBody = await errorReader.ReadToEndAsync();
            return Results.Content(errorBody, contentType, statusCode: (int)responseMessage.StatusCode);
        }
        catch (JsonException)
        {
            return Results.BadRequest("Invalid JSON format sent by client.");
        }
    }

    /// <summary>
    /// Re-constructs the <c>messages</c> array so that <c>role</c> is always the first
    /// property of each message object. The MAF <c>ChatCompletionRequestMessage</c>
    /// polymorphic deserializer requires the type discriminator (<c>role</c>) to appear
    /// before other properties; failing to do so causes a 500 during deserialization.
    /// </summary>
    /// <param name="rawJson">The raw JSON payload from the incoming request.</param>
    /// <returns>The rewritten JSON with compliant message ordering.</returns>
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
                    var contentVal = msgObj["content"]?.ToString() ?? "";

                    var compliantMessageObj = new JsonObject
                            {
                                { "role", roleVal },
                                { "content", contentVal }
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