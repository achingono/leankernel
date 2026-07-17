using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Mcp;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LeanKernel.Tests.Unit.Mcp;

public class McpToolProviderTests
{
    [Fact]
    public async Task DiscoverToolsAsync_WithNoServers_ReturnsEmpty()
    {
        var settings = new AgentSettings
        {
            Tools = new ToolSettings
            {
                McpServers = []
            }
        };

        var provider = new McpToolProvider(
            Options.Create(settings),
            NullLogger<McpToolProvider>.Instance);

        var tools = await provider.DiscoverToolsAsync();
        tools.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverToolsAsync_WithDisabledServer_ReturnsEmpty()
    {
        var settings = new AgentSettings
        {
            Tools = new ToolSettings
            {
                McpServers =
                [
                    new McpServerSettings
                    {
                        Name = "disabled-server",
                        Endpoint = "http://localhost:9999",
                        Enabled = false,
                    }
                ]
            }
        };

        var provider = new McpToolProvider(
            Options.Create(settings),
            NullLogger<McpToolProvider>.Instance);

        var tools = await provider.DiscoverToolsAsync();
        tools.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverToolsAsync_WithUnreachableNonRequiredServer_SkipsGracefully()
    {
        var settings = new AgentSettings
        {
            Tools = new ToolSettings
            {
                McpServers =
                [
                    new McpServerSettings
                    {
                        Name = "unreachable",
                        Endpoint = "http://127.0.0.1:1",
                        Enabled = true,
                        Required = false,
                        ConnectionTimeoutSeconds = 1,
                    }
                ]
            }
        };

        var provider = new McpToolProvider(
            Options.Create(settings),
            NullLogger<McpToolProvider>.Instance);

        var tools = await provider.DiscoverToolsAsync();
        tools.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverToolsAsync_WithRequiredUnreachableServer_Throws()
    {
        var settings = new AgentSettings
        {
            Tools = new ToolSettings
            {
                McpServers =
                [
                    new McpServerSettings
                    {
                        Name = "required-unreachable",
                        Endpoint = "http://127.0.0.1:1",
                        Enabled = true,
                        Required = true,
                        ConnectionTimeoutSeconds = 1,
                    }
                ]
            }
        };

        var provider = new McpToolProvider(
            Options.Create(settings),
            NullLogger<McpToolProvider>.Instance);

        await provider.Invoking(p => p.DiscoverToolsAsync())
            .Should().ThrowAsync<Exception>();
    }
}
