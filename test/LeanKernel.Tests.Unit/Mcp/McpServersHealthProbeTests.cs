using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Mcp;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace LeanKernel.Tests.Unit.Mcp;

public class McpServersHealthProbeTests
{
    [Fact]
    public async Task ProbeAsync_WithNoEnabledServers_ReturnsHealthy()
    {
        var settings = Options.Create(new AgentSettings
        {
            Tools = new ToolSettings
            {
                McpServers =
                [
                    new McpServerSettings
                    {
                        Name = "webwright",
                        Endpoint = "http://localhost:8000/mcp",
                        Enabled = false,
                    }
                ]
            }
        });

        var probe = new McpServersHealthProbe(settings, NullLoggerFactory.Instance);
        var result = await probe.ProbeAsync();

        result.IsHealthy.Should().BeTrue();
        result.Message.Should().Contain("No enabled MCP servers");
    }

    [Fact]
    public async Task ProbeAsync_WithUnreachableServer_ReturnsUnhealthy()
    {
        var settings = Options.Create(new AgentSettings
        {
            Tools = new ToolSettings
            {
                McpServers =
                [
                    new McpServerSettings
                    {
                        Name = "webwright",
                        Endpoint = "http://127.0.0.1:1",
                        Enabled = true,
                        ConnectionTimeoutSeconds = 1,
                    }
                ]
            }
        });

        var probe = new McpServersHealthProbe(settings, NullLoggerFactory.Instance);
        var result = await probe.ProbeAsync();

        result.IsHealthy.Should().BeFalse();
        result.Detail.Should().Contain("webwright");
    }
}