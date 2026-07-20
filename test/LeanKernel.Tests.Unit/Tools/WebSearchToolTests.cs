using System.Net;

using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.BuiltIn;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class WebSearchToolTests
{
    private static IServiceScopeFactory BuildScopeFactory(HttpMessageHandler? handler = null)
    {
        var services = new ServiceCollection();
        services.Configure<AgentSettings>(opts =>
        {
            opts.Tools.WebSearch.ApiKeyEnv = "TEST_BRAVE_API_KEY_NOTSET";
            opts.Tools.WebSearch.AllowHosts = ["api.search.brave.com", "api.duckduckgo.com"];
        });

        if (handler is not null)
        {
            services.AddHttpClient("web-search").ConfigurePrimaryHttpMessageHandler(() => handler);
        }
        else
        {
            services.AddHttpClient("web-search");
        }

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

    private static IServiceScopeFactory BuildScopeFactoryWithBraveKey(HttpMessageHandler handler)
    {
        Environment.SetEnvironmentVariable("TEST_BRAVE_KEY_SET", "test-brave-api-key");
        var services = new ServiceCollection();
        services.Configure<AgentSettings>(opts =>
        {
            opts.Tools.WebSearch.ApiKeyEnv = "TEST_BRAVE_KEY_SET";
        });
        services.AddHttpClient("web-search").ConfigurePrimaryHttpMessageHandler(() => handler);
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
    public async Task WebSearch_DuckDuckGo_WithResults_ReturnsOutput()
    {
        var ddgJson = """{"AbstractText":"A summary","Answer":"An answer","RelatedTopics":[{"Text":"Related topic one"}]}""";
        var mockHandler = new MockHttpHandler(HttpStatusCode.OK, ddgJson);

        var tool = WebSearchTool.Create(BuildScopeFactory(mockHandler));
        var args = new Dictionary<string, object?> { ["query"] = "test query" };

        var result = await tool.Handler(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("A summary");
    }

    [Fact]
    public async Task WebSearch_DuckDuckGo_EmptyResults_ReturnsNoResults()
    {
        var ddgJson = """{"AbstractText":"","Answer":"","RelatedTopics":[]}""";
        var mockHandler = new MockHttpHandler(HttpStatusCode.OK, ddgJson);

        var tool = WebSearchTool.Create(BuildScopeFactory(mockHandler));
        var args = new Dictionary<string, object?> { ["query"] = "obscure query with no results" };

        var result = await tool.Handler(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("No results");
    }

    [Fact]
    public async Task WebSearch_BraveSearch_WithResults_ReturnsOutput()
    {
        var braveJson = """{"web":{"results":[{"title":"Result Title","description":"Desc","url":"https://example.com"}]}}""";
        var mockHandler = new MockHttpHandler(HttpStatusCode.OK, braveJson);

        var tool = WebSearchTool.Create(BuildScopeFactoryWithBraveKey(mockHandler));
        var args = new Dictionary<string, object?> { ["query"] = "brave query" };

        var result = await tool.Handler(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Result Title");
    }

    [Fact]
    public async Task WebSearch_BraveSearch_FallsBackToDuckDuckGo_OnError()
    {
        var callCount = 0;
        var handler = new DelegatingHttpHandler(req =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"AbstractText":"Fallback result","Answer":"","RelatedTopics":[]}""")
            };
        });

        var tool = WebSearchTool.Create(BuildScopeFactoryWithBraveKey(handler));
        var args = new Dictionary<string, object?> { ["query"] = "fallback query" };

        var result = await tool.Handler(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task WebSearch_HttpException_ReturnsError()
    {
        var handler = new DelegatingHttpHandler(_ => throw new HttpRequestException("connection refused"));

        var tool = WebSearchTool.Create(BuildScopeFactory(handler));
        var args = new Dictionary<string, object?> { ["query"] = "test" };

        var result = await tool.Handler(args, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    private sealed class MockHttpHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
    }

    private sealed class DelegatingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}