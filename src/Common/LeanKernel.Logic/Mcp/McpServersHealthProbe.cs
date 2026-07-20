using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Mcp;

/// <summary>
/// Aggregated health probe for all configured MCP servers.
/// </summary>
public sealed class McpServersHealthProbe : IProviderHealthProbe
{
    private readonly IOptions<AgentSettings> _settings;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServersHealthProbe"/> class.
    /// </summary>
    /// <param name="settings">Agent settings containing MCP server configuration.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public McpServersHealthProbe(IOptions<AgentSettings> settings, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Gets the provider name for this probe.
    /// </summary>
    public string ProviderName => "mcp";

    /// <summary>
    /// Probes all configured MCP servers and returns an aggregated health result.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ProviderProbeResult"/> indicating the overall health of MCP servers.</returns>
    public async Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        var servers = _settings.Value.Tools.McpServers.Where(s => s.Enabled).ToList();
        if (servers.Count == 0)
        {
            return ProviderProbeResult.Healthy("No enabled MCP servers are configured.");
        }

        var failures = new List<string>();
        foreach (var server in servers)
        {
            var probe = new McpServerHealthProbe(server, _loggerFactory.CreateLogger<McpServerHealthProbe>());
            var result = await probe.ProbeAsync(ct).ConfigureAwait(false);
            if (!result.IsHealthy)
            {
                failures.Add($"{server.Name}: {result.Message}");
            }
        }

        if (failures.Count == 0)
        {
            return ProviderProbeResult.Healthy($"All {servers.Count} MCP server(s) are reachable.");
        }

        return ProviderProbeResult.Unhealthy(
            "One or more MCP servers are unhealthy.",
            string.Join(" | ", failures));
    }
}