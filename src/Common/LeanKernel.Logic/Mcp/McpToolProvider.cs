using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LeanKernel.Logic.Mcp;

/// <summary>
/// Discovers tools from pre-configured MCP server endpoints using the official
/// MCP C# SDK and returns them as LeanKernel <see cref="ToolDefinition"/> adapters.
/// </summary>
public sealed class McpToolProvider : IMcpToolProvider
{
    private readonly IReadOnlyList<McpServerSettings> _servers;
    private readonly ILogger<McpToolProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpToolProvider"/> class.
    /// </summary>
    /// <param name="settings">The agent settings containing MCP server configurations.</param>
    /// <param name="logger">The logger for diagnostics.</param>
    public McpToolProvider(IOptions<AgentSettings> settings, ILogger<McpToolProvider> logger)
    {
        _servers = settings.Value.Tools.McpServers;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolDefinition>> DiscoverToolsAsync(CancellationToken ct = default)
    {
        var allTools = new List<ToolDefinition>();

        foreach (var server in _servers.Where(s => s.Enabled))
        {
            try
            {
                var tools = await DiscoverServerToolsAsync(server, ct).ConfigureAwait(false);
                allTools.AddRange(tools);
                _logger.LogInformation(
                    "MCP server '{Name}' discovered {Count} tool(s).",
                    server.Name, tools.Count);
            }
            catch (Exception ex) when (!server.Required)
            {
                _logger.LogWarning(
                    ex,
                    "MCP server '{Name}' tool discovery failed. Server is not required — continuing without its tools.",
                    server.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Required MCP server '{Name}' tool discovery failed. Throwing because server is marked required.",
                    server.Name);
                throw;
            }
        }

        return allTools;
    }

    private static async Task<IReadOnlyList<ToolDefinition>> DiscoverServerToolsAsync(
        McpServerSettings server, CancellationToken ct)
    {
        var transport = CreateTransport(server);
        await using var client = await McpClient.CreateAsync(transport, null, null, ct).ConfigureAwait(false);

        var mcpTools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        var definitions = new List<ToolDefinition>(mcpTools.Count);

        foreach (var mcpTool in mcpTools)
        {
            definitions.Add(McpToolDefinitionAdapter.CreateToolDefinition(mcpTool, server.Name));
        }

        return definitions;
    }

    private static HttpClientTransport CreateTransport(McpServerSettings server)
    {
        var endpoint = new Uri(server.Endpoint);
        var transportMode = ParseTransportMode(server.TransportMode);

        var options = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = transportMode,
            ConnectionTimeout = TimeSpan.FromSeconds(Math.Max(1, server.ConnectionTimeoutSeconds)),
        };

        if (server.AdditionalHeaders.Count > 0)
        {
            options.AdditionalHeaders = new Dictionary<string, string>(server.AdditionalHeaders);
        }

        return new HttpClientTransport(options);
    }

    private static HttpTransportMode ParseTransportMode(string mode)
    {
        if (mode.Equals("Sse", StringComparison.OrdinalIgnoreCase))
            return HttpTransportMode.Sse;
        if (mode.Equals("StreamableHttp", StringComparison.OrdinalIgnoreCase))
            return HttpTransportMode.StreamableHttp;
        return HttpTransportMode.AutoDetect;
    }
}
