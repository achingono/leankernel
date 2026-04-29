using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Thinker.Middleware;

/// <summary>
/// IChatClient middleware that logs tool/function invocations.
/// Applied at the chat client level so all agents' tool calls are captured.
/// </summary>
public sealed class FunctionLoggingMiddleware
{
    private readonly ILogger<FunctionLoggingMiddleware> _logger;

    public FunctionLoggingMiddleware(ILogger<FunctionLoggingMiddleware> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Wrap a chat client with function invocation logging.
    /// Intercepts responses to log any tool call messages.
    /// </summary>
    public IChatClient Wrap(IChatClient inner)
    {
        return new ChatClientBuilder(inner)
            .Use(
                getResponseFunc: async (messages, options, innerClient, ct) =>
                {
                    var response = await innerClient.GetResponseAsync(messages, options, ct);

                    // Log any tool call content in the response
                    foreach (var msg in response.Messages)
                    {
                        foreach (var content in msg.Contents)
                        {
                            if (content is FunctionCallContent functionCall)
                            {
                                _logger.LogInformation(
                                    "Tool call: {Name} args={Args}",
                                    functionCall.Name,
                                    functionCall.Arguments?.Count ?? 0);
                            }
                            else if (content is FunctionResultContent functionResult)
                            {
                                _logger.LogInformation(
                                    "Tool result: {CallId} result={Result}",
                                    functionResult.CallId,
                                    Truncate(functionResult.Result?.ToString(), 200));
                            }
                        }
                    }

                    return response;
                },
                getStreamingResponseFunc: null)
            .Build();
    }

    private static string Truncate(string? value, int maxLength) =>
        value is null ? "(null)"
        : value.Length <= maxLength ? value
        : value[..maxLength] + "...";
}
