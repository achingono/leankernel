using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.BuiltIn.Browser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Tools;

public class WebwrightClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SubmitRunAsync_sends_post_to_runs_endpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new BrowserRunSubmissionResponse("run-1", "queued", DateTimeOffset.UtcNow, null),
                    options: JsonOptions)
            };
        });

        var client = CreateClient(handler);
        var request = new BrowserRunTaskRequest("click the button", null, null, null, null);

        var response = await client.SubmitRunAsync(request, CancellationToken.None);

        response.RunId.Should().Be("run-1");
        response.Status.Should().Be("queued");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.ToString().Should().EndWith("runs");
    }

    [Fact]
    public async Task GetRunAsync_sends_get_to_runs_endpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new BrowserRunStatusResponse("run-42", "completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null, [], null),
                    options: JsonOptions)
            };
        });

        var client = CreateClient(handler);

        var response = await client.GetRunAsync("run-42", CancellationToken.None);

        response.RunId.Should().Be("run-42");
        response.Status.Should().Be("completed");
        capturedRequest!.Method.Should().Be(HttpMethod.Get);
        capturedRequest.RequestUri!.ToString().Should().Contain("runs/run-42");
    }

    [Fact]
    public async Task CancelRunAsync_sends_delete_to_runs_endpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new BrowserCancelRunResponse("run-99", "cancelling", "ok"),
                    options: JsonOptions)
            };
        });

        var client = CreateClient(handler);

        var response = await client.CancelRunAsync("run-99", CancellationToken.None);

        response.RunId.Should().Be("run-99");
        response.Status.Should().Be("cancelling");
        capturedRequest!.Method.Should().Be(HttpMethod.Delete);
        capturedRequest.RequestUri!.ToString().Should().Contain("runs/run-99");
    }

    [Fact]
    public async Task SubmitRunAsync_throws_webwright_exception_on_error_envelope()
    {
        var errorEnvelope = new WebwrightError("SERVICE_UNAVAILABLE", "Sidecar overloaded");
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = JsonContent.Create(errorEnvelope, options: JsonOptions)
        });

        var client = CreateClient(handler);
        var request = new BrowserRunTaskRequest("do something", null, null, null, null);

        var act = () => client.SubmitRunAsync(request, CancellationToken.None);

        var ex = await act.Should().ThrowExactlyAsync<WebwrightException>();
        ex.Which.Code.Should().Be("SERVICE_UNAVAILABLE");
        ex.Which.Message.Should().Be("Sidecar overloaded");
        ex.Which.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task GetRunAsync_throws_webwright_exception_on_http_status_without_envelope()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("")
        });

        var client = CreateClient(handler);

        var act = () => client.GetRunAsync("run-missing", CancellationToken.None);

        var ex = await act.Should().ThrowExactlyAsync<WebwrightException>();
        ex.Which.Code.Should().Be("NOT_FOUND");
        ex.Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetArtifactAsync_throws_on_binary_content_exceeding_limit()
    {
        var largeBytes = new byte[100];
        Random.Shared.NextBytes(largeBytes);
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(largeBytes)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream") }
            }
        });

        var client = CreateClient(handler);

        var act = () => client.GetArtifactAsync("run-1", "art-1", maxBytes: 10, CancellationToken.None);

        var ex = await act.Should().ThrowExactlyAsync<WebwrightException>();
        ex.Which.Code.Should().Be("LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task GetArtifactAsync_truncates_text_content_within_limit()
    {
        var textBytes = System.Text.Encoding.UTF8.GetBytes("hello world");
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(textBytes)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") }
            }
        });

        var client = CreateClient(handler);

        var artifact = await client.GetArtifactAsync("run-1", "art-1", maxBytes: 5, CancellationToken.None);

        artifact.Bytes.Should().HaveCount(5);
        artifact.Truncated.Should().BeTrue();
        artifact.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task GetArtifactAsync_returns_full_content_when_within_limit()
    {
        var textBytes = System.Text.Encoding.UTF8.GetBytes("hi");
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(textBytes)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") }
            }
        });

        var client = CreateClient(handler);

        var artifact = await client.GetArtifactAsync("run-1", "art-1", maxBytes: 100, CancellationToken.None);

        artifact.Bytes.Should().BeEquivalentTo(textBytes);
        artifact.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitRunAsync_throws_argument_exception_when_request_is_null()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        var client = CreateClient(handler);

        var act = () => client.SubmitRunAsync(null!, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetRunAsync_throws_argument_exception_when_run_id_is空白()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        var client = CreateClient(handler);

        var act = () => client.GetRunAsync("  ", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetArtifactAsync_throws_when_max_bytes_is_non_positive()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        var client = CreateClient(handler);

        var act = () => client.GetArtifactAsync("run-1", "art-1", maxBytes: 0, CancellationToken.None);

        await act.Should().ThrowExactlyAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task WebwrightHealthProbe_returns_healthy_when_probe_succeeds()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = CreateHealthProbe(handler, enabled: true, probeEnabled: true);

        var result = await probe.ProbeAsync(CancellationToken.None);

        result.IsHealthy.Should().BeTrue();
        result.Message.Should().Contain("succeeded");
    }

    [Fact]
    public async Task WebwrightHealthProbe_returns_unhealthy_when_probe_fails()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var probe = CreateHealthProbe(handler, enabled: true, probeEnabled: true);

        var result = await probe.ProbeAsync(CancellationToken.None);

        result.IsHealthy.Should().BeFalse();
        result.Message.Should().Contain("503");
    }

    [Fact]
    public async Task WebwrightHealthProbe_returns_healthy_when_disabled()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        var probe = CreateHealthProbe(handler, enabled: false, probeEnabled: true);

        var result = await probe.ProbeAsync(CancellationToken.None);

        result.IsHealthy.Should().BeTrue();
        result.Message.Should().Contain("disabled");
    }

    [Fact]
    public async Task WebwrightHealthProbe_returns_healthy_when_probe_setting_disabled()
    {
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called"));
        var probe = CreateHealthProbe(handler, enabled: true, probeEnabled: false);

        var result = await probe.ProbeAsync(CancellationToken.None);

        result.IsHealthy.Should().BeTrue();
        result.Message.Should().Contain("disabled");
    }

    [Fact]
    public async Task WebwrightHealthProbe_returns_unhealthy_on_exception()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var probe = CreateHealthProbe(handler, enabled: true, probeEnabled: true);

        var result = await probe.ProbeAsync(CancellationToken.None);

        result.IsHealthy.Should().BeFalse();
        result.Message.Should().Contain("failed");
    }

    [Fact]
    public void WebwrightHealthProbe_provider_name_is_webwright()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = CreateHealthProbe(handler, enabled: true, probeEnabled: true);

        probe.ProviderName.Should().Be("webwright");
    }

    private static WebwrightClient CreateClient(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(WebwrightClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .ConfigureHttpClient(c => c.BaseAddress = new Uri("http://localhost:9999"));
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        return new WebwrightClient(factory);
    }

    private static WebwrightHealthProbe CreateHealthProbe(
        HttpMessageHandler handler,
        bool enabled,
        bool probeEnabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient(WebwrightClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .ConfigureHttpClient(c => c.BaseAddress = new Uri("http://localhost:9999"));
        services.Configure<AgentSettings>(config =>
        {
            config.Tools.Webwright.Enabled = enabled;
            config.Tools.Webwright.HealthProbe.Enabled = probeEnabled;
            config.Tools.Webwright.BaseUrl = "http://localhost:9999";
        });
        var provider = services.BuildServiceProvider();
        return new WebwrightHealthProbe(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IOptions<AgentSettings>>(),
            provider.GetRequiredService<ILogger<WebwrightHealthProbe>>());
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
