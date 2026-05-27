using System.Net;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Tools.BuiltIn.Internet;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tests.Unit.Tools;

public class HttpRequestToolTests
{
    [Fact]
    public async Task HttpRequestTool_returns_validation_error_when_url_is_missing()
    {
        var tool = HttpRequestTool.Create(CreateScopeFactory(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"))));

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("URL is required");
    }

    [Fact]
    public async Task HttpRequestTool_blocks_localhost_urls()
    {
        var tool = HttpRequestTool.Create(CreateScopeFactory(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"))));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "http://localhost:5000/test" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Localhost URLs are not allowed");
    }

    [Fact]
    public async Task HttpRequestTool_returns_validation_error_for_invalid_method()
    {
        var tool = HttpRequestTool.Create(CreateScopeFactory(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"))));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "https://example.com", ["method"] = "TRACE" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Method must be one of: GET, POST, PUT, PATCH, DELETE, HEAD");
    }

    [Fact]
    public async Task HttpRequestTool_returns_bounded_json_output_for_successful_response()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello world")
            };
            response.Headers.Add("X-Test", "yes");
            return response;
        });

        var tool = HttpRequestTool.Create(CreateScopeFactory(handler));

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/start",
            ["query"] = new Dictionary<string, object?> { ["q"] = "lean kernel" },
            ["headers"] = new Dictionary<string, object?> { ["X-Client"] = "unit-test" }
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.AbsoluteUri.Should().Contain("q=lean%20kernel");
        capturedRequest.Headers.TryGetValues("X-Client", out var headerValues).Should().BeTrue();
        headerValues.Should().ContainSingle().Which.Should().Be("unit-test");

        using var output = JsonDocument.Parse(result.Output!);
        output.RootElement.GetProperty("statusCode").GetInt32().Should().Be(200);
        output.RootElement.GetProperty("reasonPhrase").GetString().Should().Be("OK");
        output.RootElement.GetProperty("content").GetString().Should().Be("hello world");
        output.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
        output.RootElement.GetProperty("responseHeaders").GetProperty("X-Test").GetString().Should().Be("yes");
    }

    [Fact]
    public async Task HttpRequestTool_follows_redirects_hop_by_hop_and_revalidates()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            callCount++;

            if (request.RequestUri!.AbsoluteUri == "https://example.com/start")
            {
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://example.com/final") }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("redirect ok")
            };
        });

        var tool = HttpRequestTool.Create(CreateScopeFactory(handler));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "https://example.com/start" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        callCount.Should().Be(2);
        using var output = JsonDocument.Parse(result.Output!);
        output.RootElement.GetProperty("content").GetString().Should().Be("redirect ok");
    }

    [Fact]
    public async Task HttpRequestTool_blocks_redirect_to_localhost()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("http://localhost/internal") }
        });

        var tool = HttpRequestTool.Create(CreateScopeFactory(handler));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["url"] = "https://example.com/start" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Localhost URLs are not allowed");
    }

    [Fact]
    public async Task HttpRequestTool_does_not_follow_redirect_when_disabled()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.com/final") },
                Content = new StringContent("go elsewhere")
            };
        });

        var tool = HttpRequestTool.Create(CreateScopeFactory(handler));

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/start",
            ["follow_redirects"] = false
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        callCount.Should().Be(1);
        using var output = JsonDocument.Parse(result.Output!);
        output.RootElement.GetProperty("statusCode").GetInt32().Should().Be(302);
        output.RootElement.GetProperty("content").GetString().Should().Be("go elsewhere");
    }

    [Fact]
    public async Task HttpRequestTool_truncates_output_content_to_max_output_chars()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("0123456789")
        });

        var tool = HttpRequestTool.Create(CreateScopeFactory(handler));

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/data",
            ["max_output_chars"] = 5
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        using var output = JsonDocument.Parse(result.Output!);
        output.RootElement.GetProperty("content").GetString().Should().Be("01234");
        output.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task HttpRequestTool_serializes_object_body_as_json_with_default_content_type()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedPayload = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            capturedPayload = request.Content is null
                ? null
                : request.Content.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("done")
            };
        });

        var tool = HttpRequestTool.Create(CreateScopeFactory(handler));

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/post",
            ["method"] = "POST",
            ["body"] = new Dictionary<string, object?> { ["name"] = "lean" }
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Content.Should().NotBeNull();
        capturedRequest.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        capturedPayload.Should().Be("""{"name":"lean"}""");
    }

    private static IServiceScopeFactory CreateScopeFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
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
