using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;

using Microsoft.Extensions.Logging;

namespace LeanKernel.Gateway.Memory;

/// <summary>
/// Contract for low-level GBrain MCP transport.
/// </summary>
public interface IGBrainMcpClient
{
    /// <summary>
    /// Calls a named MCP tool and returns the unwrapped JSON result payload.
    /// </summary>
    Task<JsonElement?> CallToolAsync(string toolName, object? args = null, CancellationToken ct = default);
}

/// <summary>
/// Low-level JSON-RPC MCP client that communicates with the GBrain service over HTTP.
/// Supports both direct JSON and SSE responses.
/// </summary>
public sealed class GBrainMcpClient : IGBrainMcpClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<GBrainMcpClient> _logger;
    private int _requestId;

    public GBrainMcpClient(HttpClient httpClient, ILogger<GBrainMcpClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<JsonElement?> CallToolAsync(string toolName, object? args = null, CancellationToken ct = default)
    {
        var requestId = Interlocked.Increment(ref _requestId);

        var request = new McpRequest
        {
            Id = requestId,
            Method = "tools/call",
            Params = new McpToolCallParams
            {
                Name = toolName,
                Arguments = args
            }
        };

        _logger.LogDebug("GBrain MCP call: {Tool} (id={Id})", toolName, requestId);

        var result = await SendMcpRequestAsync(request, ct).ConfigureAwait(false);

        if (result?.Error is not null)
        {
            _logger.LogWarning("GBrain MCP error: {Code} {Message}", result.Error.Code, result.Error.Message);
            throw new GBrainException(result.Error.Message, result.Error.Code);
        }

        if (result?.Result is null)
        {
            return null;
        }

        return UnwrapToolResult(result.Result.Value);
    }

    private async Task<McpResponse?> SendMcpRequestAsync(McpRequest request, CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync(string.Empty, request, SerializerOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;

        if (string.Equals(contentType, Constants.Http.TextEventStream, StringComparison.OrdinalIgnoreCase))
        {
            return await ReadSseResponseAsync(response, ct).ConfigureAwait(false);
        }

        return await response.Content.ReadFromJsonAsync<McpResponse>(SerializerOptions, ct).ConfigureAwait(false);
    }

    private static async Task<McpResponse?> ReadSseResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        McpResponse? lastResponse = null;

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var json = line[6..];
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            lastResponse = JsonSerializer.Deserialize<McpResponse>(json, SerializerOptions);
        }

        return lastResponse;
    }

    private static JsonElement? UnwrapToolResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
        {
            return result.Clone();
        }

        var hasEnvelope = result.TryGetProperty("content", out _)
            || result.TryGetProperty("structuredContent", out _)
            || result.TryGetProperty("isError", out _);

        if (!hasEnvelope)
        {
            return result.Clone();
        }

        var toolResult = result.Deserialize<McpToolResult>(SerializerOptions);
        if (toolResult is null)
        {
            return null;
        }

        if (toolResult.IsError)
        {
            throw new GBrainException(toolResult.GetMessage());
        }

        if (toolResult.StructuredContent is { } structuredContent
            && structuredContent.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            return structuredContent.Clone();
        }

        var text = toolResult.Content
            .FirstOrDefault(item => item.Type.Equals("text", StringComparison.OrdinalIgnoreCase))?
            .Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(text, SerializerOptions));
            return document.RootElement.Clone();
        }
    }
}

internal sealed class McpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("method")]
    public required string Method { get; set; }

    [JsonPropertyName("params")]
    public object? Params { get; set; }
}

internal sealed class McpToolCallParams
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("arguments")]
    public object? Arguments { get; set; }
}

internal sealed class McpResponse
{
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public McpError? Error { get; set; }
}

internal sealed class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

internal sealed class McpToolResult
{
    [JsonPropertyName("content")]
    public List<McpContentItem> Content { get; set; } = [];

    [JsonPropertyName("structuredContent")]
    public JsonElement? StructuredContent { get; set; }

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }

    public string GetMessage()
    {
        var text = Content.FirstOrDefault(item => item.Type.Equals("text", StringComparison.OrdinalIgnoreCase))?.Text;
        return string.IsNullOrWhiteSpace(text) ? "GBrain tool call failed." : text;
    }
}

internal sealed class McpContentItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
    /// <summary>
    /// Initializes a new instance of the <see cref="GBrainMcpClient"/> class.
    /// </summary>
    /// <inheritdoc />
