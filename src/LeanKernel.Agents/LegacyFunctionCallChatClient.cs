using System.Text.Json;
using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Agents;

/// <summary>
/// A chat client that supports replaying legacy function-call payloads.
/// </summary>
internal sealed class LegacyFunctionCallChatClient : IChatClient
{
    private const int MaxReplayAttempts = 1;

    private readonly IChatClient _functionInvokingClient;
    private readonly IChatClient _rawClient;
    private readonly IToolExecutor _toolExecutor;
    private readonly ILogger<LegacyFunctionCallChatClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LegacyFunctionCallChatClient"/> class.
    /// </summary>
    /// <param name="functionInvokingClient">The client for invoking functions.</param>
    /// <param name="rawClient">The raw chat client for direct messaging.</param>
    /// <param name="toolExecutor">The tool executor.</param>
    /// <param name="logger">The logger.</param>
    public LegacyFunctionCallChatClient(
        IChatClient functionInvokingClient,
        IChatClient rawClient,
        IToolExecutor toolExecutor,
        ILogger<LegacyFunctionCallChatClient> logger)
    {
        _functionInvokingClient = functionInvokingClient ?? throw new ArgumentNullException(nameof(functionInvokingClient));
        _rawClient = rawClient ?? throw new ArgumentNullException(nameof(rawClient));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return GetResponseCoreAsync(messages, options, cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in _functionInvokingClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
        => _functionInvokingClient.GetService(serviceType, serviceKey);

    /// <inheritdoc/>
    public void Dispose()
        => _functionInvokingClient.Dispose();

    private async Task<ChatResponse> GetResponseCoreAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var originalMessages = messages as IList<ChatMessage> ?? messages.ToList();
        var response = await _functionInvokingClient.GetResponseAsync(originalMessages, options, cancellationToken).ConfigureAwait(false);

        return await TryHandleLegacyFunctionCallAsync(response, originalMessages, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatResponse> TryHandleLegacyFunctionCallAsync(
        ChatResponse response,
        IList<ChatMessage> originalMessages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        if (!TryParseLegacyFunctionCall(response.Text, out var legacyCall))
        {
            return response;
        }

        _logger.LogDebug("Detected a legacy function-call payload for tool {ToolName}", legacyCall.Name);
        return await ReplayLegacyFunctionCallAsync(originalMessages, options, response, legacyCall, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatResponse> ReplayLegacyFunctionCallAsync(
        IList<ChatMessage> originalMessages,
        ChatOptions? options,
        ChatResponse originalResponse,
        LegacyFunctionCall legacyCall,
        CancellationToken cancellationToken)
    {
        if (!IsToolVisible(options, legacyCall.Name))
        {
            _logger.LogWarning(
                "Rejected legacy function-call replay for tool {ToolName} because it was not offered in ChatOptions.Tools",
                legacyCall.Name);
            return originalResponse;
        }

        var execution = await _toolExecutor.ExecuteAsync(legacyCall.Name, legacyCall.Parameters, cancellationToken).ConfigureAwait(false);
        if (!execution.Success)
        {
            _logger.LogWarning(
                "Legacy function-call payload for tool {ToolName} was not executed: {Error}",
                legacyCall.Name,
                execution.Error);
            return originalResponse;
        }

        var callId = Guid.NewGuid().ToString("N");
        var replayMessages = new List<ChatMessage>(originalMessages.Count + 2);
        replayMessages.AddRange(originalMessages);
        replayMessages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, legacyCall.Name, legacyCall.Parameters)]));
        replayMessages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, execution.Output ?? string.Empty)]));
        
        for (var attempt = 0; attempt < MaxReplayAttempts; attempt++)
        {
            var response = await _rawClient.GetResponseAsync(replayMessages, options, cancellationToken).ConfigureAwait(false);
            if (!TryParseLegacyFunctionCall(response.Text, out _))
            {
                return response;
            }
        }

        _logger.LogWarning(
            "Legacy replay for tool {ToolName} still returned a legacy payload; returning the tool output instead",
            legacyCall.Name);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, execution.Output ?? string.Empty));
    }

    private static bool TryParseLegacyFunctionCall(string? responseText, out LegacyFunctionCall legacyCall)
    {
        legacyCall = default!;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        var trimmed = TryExtractFencedJsonBody(responseText.Trim());
        if (trimmed is null)
        {
            return false;
        }

        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[^1] != '}')
        {
            return false;
        }

        return TryParseJsonFunctionCall(trimmed, out legacyCall);
    }

    private static string? TryExtractFencedJsonBody(string trimmed)
    {
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine)
        {
            return null;
        }

        return trimmed[(firstNewLine + 1)..lastFence].Trim();
    }

    private static bool TryParseJsonFunctionCall(string json, out LegacyFunctionCall legacyCall)
    {
        legacyCall = default!;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });

            var root = document.RootElement;
            if (!ValidateFunctionCallShape(root, out var name, out var parameters))
            {
                return false;
            }

            legacyCall = new LegacyFunctionCall(
                name.GetString()!,
                ConvertObject(parameters));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ValidateFunctionCallShape(JsonElement root, out JsonElement name, out JsonElement parameters)
    {
        name = default;
        parameters = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryGetProperty(root, "type", out var typeElement)
            || !TryGetProperty(root, "name", out var nameElement)
            || !TryGetProperty(root, "parameters", out var parametersElement))
        {
            return false;
        }

        if (root.EnumerateObject().Count() != 3)
        {
            return false;
        }

        if (typeElement.ValueKind != JsonValueKind.String || !string.Equals(typeElement.GetString(), "function", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (nameElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        if (parametersElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        name = nameElement;
        parameters = parametersElement;
        return true;
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        var property = root.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        value = property.Value;
        return true;
    }

    private static IDictionary<string, object?> ConvertObject(JsonElement element)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = ConvertValue(property.Value);
        }
        return values;
    }

    private static object? ConvertValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => ConvertObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertValue).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.GetRawText(),
        };

    private static int ToInt32(long value)
        => value switch
        {
            > int.MaxValue => int.MaxValue,
            < int.MinValue => int.MinValue,
            _ => (int)value
        };

    private static bool IsToolVisible(ChatOptions? options, string toolName)
    {
        if (options?.Tools is null || options.Tools.Count == 0)
        {
            return true;
        }

        return options.Tools
            .Select(ResolveToolName)
            .Any(name => !string.IsNullOrWhiteSpace(name)
                && string.Equals(name, toolName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveToolName(AITool tool)
    {
        var property = tool.GetType().GetProperty("Name");
        return property?.GetValue(tool) as string;
    }

    private sealed record LegacyFunctionCall(string Name, IDictionary<string, object?> Parameters);
}
