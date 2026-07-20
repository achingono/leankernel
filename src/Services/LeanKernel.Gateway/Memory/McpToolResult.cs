using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeanKernel.Gateway.Memory;

/// <summary>
/// Represents the standard MCP tool result envelope.
/// </summary>
internal sealed class McpToolResult
{
    [JsonPropertyName("content")]
    public List<McpContentItem> Content { get; set; } = [];

    [JsonPropertyName("structuredContent")]
    public JsonElement? StructuredContent { get; set; }

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }

    /// <summary>
    /// Resolves a readable error message from the tool result content.
    /// </summary>
    /// <returns>The extracted error message, or a default fallback message.</returns>
    public string GetMessage()
    {
        var text = Content.FirstOrDefault(item => item.Type.Equals("text", StringComparison.OrdinalIgnoreCase))?.Text;
        return string.IsNullOrWhiteSpace(text) ? "GBrain tool call failed." : text;
    }
}
