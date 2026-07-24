using System.Text.Json;

namespace LeanKernel.Services.Common.Interfaces;

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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task<JsonElement?> CallToolAsync(string toolName, object? args = null, CancellationToken ct = default);
}