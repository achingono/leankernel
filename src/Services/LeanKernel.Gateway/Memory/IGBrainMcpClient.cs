using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeanKernel.Gateway.Memory;

/// <summary>
/// Contract for low-level GBrain MCP transport.
/// </summary>
public interface IGBrainMcpClient
{
    /// <summary>
    /// Calls a GBrain MCP tool by name with the given arguments.
    /// </summary>
    /// <param name="toolName">The MCP tool name to invoke.</param>
    /// <param name="args">The tool arguments to serialize into the MCP request payload.</param>
    /// <param name="ct">The cancellation token for the operation.</param>
    /// <returns>The unwrapped tool result payload, if one is returned.</returns>
    Task<JsonElement?> CallToolAsync(string toolName, object? args = null, CancellationToken ct = default);
}
