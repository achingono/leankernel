using System.Net;

using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.BuiltIn.Internet;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class WebFetchToolTests
{
    [Fact]
    public async Task WebFetchTool_returns_validation_error_when_url_is_missing()
    {
        var tool = WebFetchTool.Create(CreateScopeFactory(new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called")))));

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("URL is required");
    }

    [Fact]
    public async Task WebFetchTool_returns_validation_error_when_url_is_invalid()
    {
        var tool = WebFetchTool.Create(CreateScopeFactory(new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called")))));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "not-a-url" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("URL must be an absolute HTTP or HTTPS URL");
    }

    [Fact]
    public async Task WebFetchTool_blocks_localhost_urls()
    {
        var tool = WebFetchTool.Create(CreateScopeFactory(new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called")))));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "http://localhost:8080/test" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Private, loopback, or link-local URLs are not allowed");
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
    public async Task WebFetchTool_follows_redirects()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri == "https://example.com/start")
            {
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://example.com/final") }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("redirect target")
            };
        });

        var tool = WebFetchTool.Create(CreateScopeFactory(new HttpClient(handler)));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "https://example.com/start" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("redirect target");
    }

    [Fact]
    public async Task WebFetchTool_honors_configured_max_redirects()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.com/next") }
            };
        });

        var tool = WebFetchTool.Create(CreateScopeFactory(new HttpClient(handler), maxRedirects: 0));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "https://example.com/start" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Too many redirects");
        callCount.Should().Be(1);
    }

    private static IServiceScopeFactory CreateScopeFactory(HttpClient httpClient, string? tempRoot = null, string? pythonExecutable = null, int? maxRedirects = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(httpClient);
        if (maxRedirects is not null)
        {
            services.Configure<AgentSettings>(options => options.Tools.Internet.MaxRedirects = maxRedirects.Value);
        }

        services.Configure<FileSettings>(config =>
        {
            config.RootPath = tempRoot ?? CreateTempDirectory();
            config.ScratchRoot = Path.Combine(config.RootPath, ".scratch");
            if (!string.IsNullOrWhiteSpace(pythonExecutable))
            {
                config.PythonExecutable = pythonExecutable;
            }
        });

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "leankernel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory = responseFactory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}