using LeanKernel.Logic.Tools;

namespace LeanKernel.Logic.Mcp;

/// <summary>
/// Discovers tools from pre-configured MCP server endpoints and returns them
/// as LeanKernel <see cref="ToolDefinition"/> adapters.
/// </summary>
public interface IMcpToolProvider
{
    /// <summary>
    /// Discovers tools from all configured MCP servers and returns them as
    /// LeanKernel tool definitions ready for registration in the tool registry.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The discovered tool definitions.</returns>
    Task<IReadOnlyList<ToolDefinition>> DiscoverToolsAsync(CancellationToken ct = default);
}