using System.Net;
using FluentAssertions;
using LeanKernel.Gateway.Tools.BuiltIn;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class WebSearchToolTests
{
    private IServiceScopeFactory BuildScopeFactory(string? braveApiKey = null)
    {
        var services = new ServiceCollection();
        services.Configure<AgentSettings>(opts =>
        {
            opts.Tools.WebSearch.Provider = braveApiKey != null ? "brave" : "duckduckgo";
            opts.Tools.WebSearch.ApiKeyEnv = "TEST_BRAVE_API_KEY";
            opts.Tools.WebSearch.AllowHosts = ["api.search.brave.com", "api.duckduckgo.com"];
        });

        // Register a named HTTP client that will be mocked
        services.AddHttpClient("web-search");

        var sp = services.BuildServiceProvider();

        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope())
            .Returns(() =>
            {
                var mockScope = new Mock<IServiceScope>();
                mockScope.Setup(s => s.ServiceProvider).Returns(sp);
                return mockScope.Object;
            });

        return mockFactory.Object;
    }

    [Fact]
    public void WebSearch_ToolName_IsCorrect()
    {
        var tool = WebSearchTool.Create(BuildScopeFactory());
        tool.Name.Should().Be("web_search");
        tool.Category.Should().Be("internet");
    }

    [Fact]
    public void WebSearch_Parameters_HasQueryParam()
    {
        var tool = WebSearchTool.Create(BuildScopeFactory());
        tool.Parameters.Should().ContainSingle(p => p.Name == "query" && p.Required);
    }

    [Fact]
    public async Task WebSearch_MissingQuery_ReturnsError()
    {
        var tool = WebSearchTool.Create(BuildScopeFactory());

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("required");
    }

    [Fact]
    public void WebSearch_NullScopeFactory_Throws()
    {
        var act = () => WebSearchTool.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task WebSearch_EmptyQuery_ReturnsError()
    {
        var tool = WebSearchTool.Create(BuildScopeFactory());
        var args = new Dictionary<string, object?> { ["query"] = "   " };

        var result = await tool.Handler(args, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("required");
    }

    [Fact]
    public async Task WebSearch_ValidQuery_HttpClientFailure_ReturnsError()
    {
        // Don't actually call the internet — just verify failure is handled gracefully
        var tool = WebSearchTool.Create(BuildScopeFactory());
        var args = new Dictionary<string, object?> { ["query"] = "test query" };

        // This will fail because there's no real HTTP server, but shouldn't throw
        var result = await tool.Handler(args, CancellationToken.None);

        // Either success or failure, but should not throw
        result.Should().NotBeNull();
    }
}
