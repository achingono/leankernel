using System.Text.Json.Serialization;

namespace LeanKernel.Services.Common.Memory;

/// <summary>
/// Represents a single content item returned in an MCP tool result.
/// </summary>
internal sealed class McpContentItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}