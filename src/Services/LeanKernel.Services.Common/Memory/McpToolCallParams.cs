using System.Text.Json.Serialization;

namespace LeanKernel.Services.Common.Memory;

/// <summary>
/// Represents the parameters for an MCP tool call.
/// </summary>
internal sealed class McpToolCallParams
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("arguments")]
    public object? Arguments { get; set; }
}