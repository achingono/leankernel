using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Tools.BuiltIn.Internet;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task WebFetchTool_downloads_binary_content_and_extracts_text()
    {
        var tempRoot = CreateTempDirectory();
        var fakePython = CreateFakePythonScript(tempRoot, "OCR TEXT FROM DOWNLOAD");

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = CreateBinaryContent()
        });

        var tool = WebFetchTool.Create(CreateScopeFactory(new HttpClient(handler), tempRoot, fakePython));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "https://example.com/file.png" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("OCR TEXT FROM DOWNLOAD");
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

    private static IServiceScopeFactory CreateScopeFactory(HttpClient httpClient, string? tempRoot = null, string? pythonExecutable = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(httpClient);
        services.Configure<LeanKernelConfig>(config =>
        {
            config.FileSystem.AllowedRoot = tempRoot ?? CreateTempDirectory();
            config.FileSystem.ScratchRoot = Path.Combine(config.FileSystem.AllowedRoot, ".scratch");
            if (!string.IsNullOrWhiteSpace(pythonExecutable))
            {
                config.FileSystem.PythonExecutable = pythonExecutable;
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

    private static string CreateFakePythonScript(string tempRoot, string output)
    {
        var scriptPath = Path.Combine(tempRoot, "fake-python.sh");
        File.WriteAllText(scriptPath, $"#!/usr/bin/env bash\ncat >/dev/null\ncat <<'EOF'\n{output}\nEOF\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return scriptPath;
    }

    private static HttpContent CreateBinaryContent()
    {
        var content = new ByteArrayContent([1, 2, 3, 4]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
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
