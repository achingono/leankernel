using System.Text.Json.Serialization;

namespace LeanKernel.Services.Common.Memory;

/// <summary>
/// Represents an error returned by the MCP transport.
/// </summary>
internal sealed class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}