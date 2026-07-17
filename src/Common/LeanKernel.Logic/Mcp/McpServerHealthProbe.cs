using LeanKernel.Logic.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace LeanKernel.Logic.Mcp;

/// <summary>
/// Health probe for MCP server endpoints.
/// Verifies that the configured MCP server is reachable and supports tool discovery.
/// </summary>
public sealed class McpServerHealthProbe : Tools.IProviderHealthProbe
{
    private readonly McpServerSettings _server;
    private readonly ILogger<McpServerHealthProbe> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerHealthProbe"/> class.
    /// </summary>
    /// <param name="server">The MCP server settings to probe.</param>
    /// <param name="logger">The logger for diagnostics.</param>
    public McpServerHealthProbe(McpServerSettings server, ILogger<McpServerHealthProbe> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ProviderName => $"mcp:{_server.Name}";

    /// <inheritdoc />
    public async Task<Tools.ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (!_server.Enabled)
        {
            return Tools.ProviderProbeResult.Healthy($"MCP server '{_server.Name}' is disabled.");
        }

        try
        {
            var transport = CreateTransport();
            await using var client = await McpClient.CreateAsync(transport, null, null, ct).ConfigureAwait(false);
            var tools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);

            _logger.LogDebug("MCP server '{Name}' probe succeeded. {Count} tool(s) available.", _server.Name, tools.Count);
            return Tools.ProviderProbeResult.Healthy(
                $"MCP server '{_server.Name}' is reachable. {tools.Count} tool(s) available.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP server '{Name}' probe failed.", _server.Name);
            return Tools.ProviderProbeResult.Unhealthy(
                $"MCP server '{_server.Name}' probe failed: {ex.Message}",
                ex.ToString());
        }
    }

    private HttpClientTransport CreateTransport()
    {
        var endpoint = new Uri(_server.Endpoint);
        var transportMode = ParseTransportMode(_server.TransportMode);

        var options = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = transportMode,
            ConnectionTimeout = TimeSpan.FromSeconds(Math.Max(1, _server.ConnectionTimeoutSeconds)),
        };

        if (_server.AdditionalHeaders.Count > 0)
        {
            options.AdditionalHeaders = new Dictionary<string, string>(_server.AdditionalHeaders);
        }

        return new HttpClientTransport(options);
    }

    private static ModelContextProtocol.Client.HttpTransportMode ParseTransportMode(string mode)
    {
        if (mode.Equals("Sse", StringComparison.OrdinalIgnoreCase))
            return ModelContextProtocol.Client.HttpTransportMode.Sse;
        if (mode.Equals("StreamableHttp", StringComparison.OrdinalIgnoreCase))
            return ModelContextProtocol.Client.HttpTransportMode.StreamableHttp;
        return ModelContextProtocol.Client.HttpTransportMode.AutoDetect;
    }
}
