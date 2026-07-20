using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeanKernel.Gateway.Memory;

/// <summary>
/// Represents a JSON-RPC MCP request payload.
/// </summary>
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
