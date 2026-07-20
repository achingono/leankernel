using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeanKernel.Gateway.Memory;

/// <summary>
/// Represents a JSON-RPC MCP response payload.
/// </summary>
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
