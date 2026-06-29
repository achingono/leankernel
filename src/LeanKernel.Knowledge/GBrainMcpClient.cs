using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Knowledge;

/// <summary>
/// Provides functionality for gbrain mcp client.
/// </summary>
public sealed class GBrainMcpClient
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

    /// <summary>
    /// Executes call tool async.
    /// </summary>
    /// <param name="toolName">The tool name.</param>
    /// <param name="args">The args.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>The operation result.</returns>
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

    /// <summary>
    /// Lists tools async.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>The operation result.</returns>
    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct = default)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var request = new McpRequest
        {
            Id = requestId,
            Method = "tools/list",
            Params = new { }
        };

        var result = await SendMcpRequestAsync(request, ct).ConfigureAwait(false);

        if (result?.Error is not null)
        {
            _logger.LogWarning("GBrain MCP error: {Code} {Message}", result.Error.Code, result.Error.Message);
            throw new GBrainException(result.Error.Message, result.Error.Code);
        }

        if (result?.Result is null)
        {
            return [];
        }

        var tools = result.Result.Value.Deserialize<McpToolListResult>(SerializerOptions);
        return tools?.Tools ?? [];
    }

    private async Task<McpResponse?> SendMcpRequestAsync(McpRequest request, CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync(string.Empty, request, SerializerOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;

        if (string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadSseResponseAsync(response, ct).ConfigureAwait(false);
        }

        // Direct JSON response
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

            var json = line[6..]; // Strip "data: " prefix
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

        var hasEnvelope = result.TryGetProperty("content", out _) ||
            result.TryGetProperty("structuredContent", out _) ||
            result.TryGetProperty("isError", out _);

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

        if (toolResult.StructuredContent is { } structuredContent &&
            structuredContent.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
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

/// <summary>
/// Provides functionality for mcp request.
/// </summary>
internal sealed class McpRequest
{
    [JsonPropertyName("jsonrpc")]
    /// <summary>
    /// Gets or sets json rpc.
    /// </summary>
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    /// <summary>
    /// Gets or sets id.
    /// </summary>
    public int Id { get; set; }

    [JsonPropertyName("method")]
    /// <summary>
    /// Gets or sets method.
    /// </summary>
    public required string Method { get; set; }

    [JsonPropertyName("params")]
    /// <summary>
    /// Gets or sets params.
    /// </summary>
    public object? Params { get; set; }
}

/// <summary>
/// Provides functionality for mcp tool call params.
/// </summary>
internal sealed class McpToolCallParams
{
    [JsonPropertyName("name")]
    /// <summary>
    /// Gets or sets name.
    /// </summary>
    public required string Name { get; set; }

    [JsonPropertyName("arguments")]
    /// <summary>
    /// Gets or sets arguments.
    /// </summary>
    public object? Arguments { get; set; }
}

/// <summary>
/// Provides functionality for mcp response.
/// </summary>
internal sealed class McpResponse
{
    [JsonPropertyName("jsonrpc")]
    /// <summary>
    /// Gets or sets json rpc.
    /// </summary>
    public string? JsonRpc { get; set; }

    [JsonPropertyName("id")]
    /// <summary>
    /// Gets or sets id.
    /// </summary>
    public int Id { get; set; }

    [JsonPropertyName("result")]
    /// <summary>
    /// Gets or sets result.
    /// </summary>
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    /// <summary>
    /// Gets or sets error.
    /// </summary>
    public McpError? Error { get; set; }
}

/// <summary>
/// Provides functionality for mcp error.
/// </summary>
internal sealed class McpError
{
    [JsonPropertyName("code")]
    /// <summary>
    /// Gets or sets code.
    /// </summary>
    public int Code { get; set; }

    [JsonPropertyName("message")]
    /// <summary>
    /// Gets or sets message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Provides functionality for mcp tool list result.
/// </summary>
internal sealed class McpToolListResult
{
    [JsonPropertyName("tools")]
    /// <summary>
    /// Gets or sets tools.
    /// </summary>
    public List<McpToolInfo> Tools { get; set; } = [];
}

/// <summary>
/// Provides functionality for mcp tool result.
/// </summary>
internal sealed class McpToolResult
{
    [JsonPropertyName("content")]
    /// <summary>
    /// Gets or sets content.
    /// </summary>
    public List<McpContentItem> Content { get; set; } = [];

    [JsonPropertyName("structuredContent")]
    /// <summary>
    /// Gets or sets structured content.
    /// </summary>
    public JsonElement? StructuredContent { get; set; }

    [JsonPropertyName("isError")]
    /// <summary>
    /// Gets or sets is error.
    /// </summary>
    public bool IsError { get; set; }

    /// <summary>
    /// Gets message.
    /// </summary>
    /// <returns>The operation result.</returns>
    public string GetMessage()
    {
        var text = Content.FirstOrDefault(item => item.Type.Equals("text", StringComparison.OrdinalIgnoreCase))?.Text;
        return string.IsNullOrWhiteSpace(text) ? "GBrain tool call failed." : text;
    }
}

/// <summary>
/// Provides functionality for mcp content item.
/// </summary>
internal sealed class McpContentItem
{
    [JsonPropertyName("type")]
    /// <summary>
    /// Gets or sets type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    /// <summary>
    /// Gets or sets text.
    /// </summary>
    public string? Text { get; set; }
}

/// <summary>
/// Provides functionality for mcp tool info.
/// </summary>
public sealed class McpToolInfo
{
    [JsonPropertyName("name")]
    /// <summary>
    /// Gets or sets name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    /// <summary>
    /// Gets or sets description.
    /// </summary>
    public string? Description { get; set; }
}
