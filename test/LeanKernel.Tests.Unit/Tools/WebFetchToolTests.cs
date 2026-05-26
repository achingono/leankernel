using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using LeanKernel.Tools.BuiltIn;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public class WebFetchToolTests
{
    [Fact]
    public async Task WebFetchTool_returns_validation_error_when_url_is_missing()
    {
        var client = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called")));
        var tool = WebFetchTool.Create(CreateScopeFactory(client));

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("URL is required");
    }

    [Fact]
    public async Task WebFetchTool_returns_validation_error_when_url_is_invalid()
    {
        var client = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called")));
        var tool = WebFetchTool.Create(CreateScopeFactory(client));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "not-a-url" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("URL must be an absolute HTTP or HTTPS URL");
    }

    [Fact]
    public async Task WebFetchTool_blocks_localhost_urls()
    {
        var client = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called")));
        var tool = WebFetchTool.Create(CreateScopeFactory(client));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "http://localhost:8080/test" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Localhost URLs are not allowed");
    }

    [Fact]
    public async Task WebFetchTool_returns_content_when_http_request_succeeds()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("hello from the web")
        });
        var tool = WebFetchTool.Create(CreateScopeFactory(new HttpClient(handler)));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "https://example.com" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("hello from the web");
    }

    [Fact]
    public async Task WebFetchTool_returns_failure_when_http_status_is_not_success()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            ReasonPhrase = "Not Found",
            Content = new StringContent("missing")
        });
        var tool = WebFetchTool.Create(CreateScopeFactory(new HttpClient(handler)));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "https://example.com/missing" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("404");
    }

    [Fact]
    public async Task WebFetchTool_rejects_non_text_content_types()
    {
        var binary = new ByteArrayContent([1, 2, 3, 4]);
        binary.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = binary
        });
        var tool = WebFetchTool.Create(CreateScopeFactory(new HttpClient(handler)));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "https://example.com/file.bin" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unsupported content type");
    }

    [Fact]
    public async Task WebFetchTool_truncates_large_content_with_notice()
    {
        var longContent = new string('a', 20_500);
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(longContent)
        });
        var tool = WebFetchTool.Create(CreateScopeFactory(new HttpClient(handler)));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "https://example.com/large" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().NotBeNull();
        result.Output!.Should().Contain("[Content truncated to 20000 characters.]");
        result.Output!.Length.Should().BeGreaterThan(20_000);
    }

    private static IServiceScopeFactory CreateScopeFactory(HttpClient httpClient)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(mock => mock.GetService(typeof(HttpClient)))
            .Returns(httpClient);

        return new TestServiceScopeFactory(serviceProvider.Object);
    }

    private sealed class TestServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public TestServiceScopeFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IServiceScope CreateScope()
        {
            return new TestServiceScope(_serviceProvider);
        }
    }

    private sealed class TestServiceScope : IServiceScope
    {
        public TestServiceScope(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}