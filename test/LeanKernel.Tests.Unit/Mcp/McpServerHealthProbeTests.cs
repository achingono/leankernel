using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Mcp;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace LeanKernel.Tests.Unit.Mcp;

public class McpServerHealthProbeTests
{
    [Fact]
    public async Task ProbeAsync_WhenDisabled_ReturnsHealthy()
    {
        var server = new McpServerSettings
        {
            Name = "test-server",
            Endpoint = "http://localhost:9999",
            Enabled = false,
        };

        var probe = new McpServerHealthProbe(server, NullLogger<McpServerHealthProbe>.Instance);
        var result = await probe.ProbeAsync();

        result.IsHealthy.Should().BeTrue();
        result.Message.Should().Contain("disabled");
    }

    [Fact]
    public void ProviderName_ReturnsMcpPrefixedName()
    {
        var server = new McpServerSettings
        {
            Name = "playwright",
            Endpoint = "http://localhost:3100",
            Enabled = true,
        };

        var probe = new McpServerHealthProbe(server, NullLogger<McpServerHealthProbe>.Instance);
        probe.ProviderName.Should().Be("mcp:playwright");
    }

    [Fact]
    public async Task ProbeAsync_WhenServerUnreachable_ReturnsUnhealthy()
    {
        var server = new McpServerSettings
        {
            Name = "unreachable",
            Endpoint = "http://127.0.0.1:1",
            Enabled = true,
            ConnectionTimeoutSeconds = 1,
        };

        var probe = new McpServerHealthProbe(server, NullLogger<McpServerHealthProbe>.Instance);
        var result = await probe.ProbeAsync();

        result.IsHealthy.Should().BeFalse();
        result.Message.Should().Contain("unreachable");
        result.Detail.Should().NotBeNullOrEmpty();
    }
}