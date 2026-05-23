using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Knowledge;

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

        using var response = await _httpClient.PostAsJsonAsync(string.Empty, request, SerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<McpResponse>(SerializerOptions, ct);
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

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken ct = default)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        var request = new McpRequest
        {
            Id = requestId,
            Method = "tools/list",
            Params = new { }
        };

        using var response = await _httpClient.PostAsJsonAsync(string.Empty, request, SerializerOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<McpResponse>(SerializerOptions, ct);
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

internal sealed class McpToolListResult
{
    [JsonPropertyName("tools")]
    public List<McpToolInfo> Tools { get; set; } = [];
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

public sealed class McpToolInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
