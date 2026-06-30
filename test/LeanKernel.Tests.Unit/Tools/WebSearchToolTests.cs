using System.Net;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Tools.BuiltIn.Internet;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tests.Unit.Tools;

public class WebSearchToolTests
{
    [Fact]
    public async Task WebSearchTool_returns_validation_error_when_query_is_missing()
    {
        var tool = WebSearchTool.Create(CreateScopeFactory(new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called")))));

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Query is required");
    }

    [Fact]
    public async Task WebSearchTool_returns_validation_error_when_query_is_empty()
    {
        var tool = WebSearchTool.Create(CreateScopeFactory(new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called")))));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["query"] = "  " }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Query is required");
    }

    [Fact]
    public async Task WebSearchTool_returns_failure_when_http_request_fails()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            ReasonPhrase = "Server Error",
        });
        var tool = WebSearchTool.Create(CreateScopeFactory(new HttpClient(handler)));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["query"] = "test query" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("500");
    }

    [Fact]
    public async Task WebSearchTool_returns_duckduckgo_abstract_when_available()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                AbstractText = "Atlas is a Titan in Greek mythology.",
                Answer = "",
            }))
        };
        var handler = new StubHttpMessageHandler(_ => response);
        var tool = WebSearchTool.Create(CreateScopeFactory(new HttpClient(handler)));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["query"] = "Atlas" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("Atlas is a Titan in Greek mythology.");
    }

    [Fact]
    public async Task WebSearchTool_returns_duckduckgo_answer_when_no_abstract()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                AbstractText = "",
                Answer = "42",
            }))
        };
        var handler = new StubHttpMessageHandler(_ => response);
        var tool = WebSearchTool.Create(CreateScopeFactory(new HttpClient(handler)));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["query"] = "meaning of life" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("42");
    }

    [Fact]
    public async Task WebSearchTool_returns_no_answer_when_neither_abstract_nor_answer_provided()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                AbstractText = "",
                Answer = "",
            }))
        };
        var handler = new StubHttpMessageHandler(_ => response);
        var tool = WebSearchTool.Create(CreateScopeFactory(new HttpClient(handler)));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["query"] = "unknown" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("No instant answer found for: unknown");
    }

    [Fact]
    public async Task WebSearchTool_create_throws_on_null_scope_factory()
    {
        Assert.Throws<ArgumentNullException>(() => WebSearchTool.Create(null!));
    }

    private static IServiceScopeFactory CreateScopeFactory(HttpClient httpClient)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(httpClient);
        services.Configure<LeanKernelConfig>(_ => { });

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
