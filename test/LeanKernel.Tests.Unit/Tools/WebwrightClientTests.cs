using System.Net;
using System.Text;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Browser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public class WebwrightClientTests
{
    [Fact]
    public async Task SubmitRunAsync_posts_to_runs_endpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var client = CreateClient(request =>
        {
            capturedRequest = request;
            capturedBody = request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult();
            return JsonResponse(HttpStatusCode.Accepted, """{"runId":"run-1","status":"queued","submittedAt":"2026-05-28T09:00:00Z","queuePosition":1}""");
        });

        var response = await client.SubmitRunAsync(new BrowserRunTaskRequest("Read", "https://example.com", "gpt-4o", "key", "idem"));

        response.RunId.Should().Be("run-1");
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
        capturedRequest.RequestUri!.ToString().Should().Be("https://browser.test/runs");
        capturedBody.Should().Contain("\"task\":\"Read\"");
        capturedBody.Should().Contain("\"startUrl\":\"https://example.com\"");
    }

    [Fact]
    public async Task CancelRunAsync_sends_delete_to_run_endpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient(request =>
        {
            capturedRequest = request;
            return JsonResponse(HttpStatusCode.OK, """{"runId":"run-1","status":"cancelled","message":"Cancellation requested."}""");
        });

        var response = await client.CancelRunAsync("run-1");

        response.Status.Should().Be("cancelled");
        capturedRequest!.Method.Should().Be(HttpMethod.Delete);
        capturedRequest.RequestUri!.ToString().Should().Be("https://browser.test/runs/run-1");
    }

    [Fact]
    public async Task GetRunAsync_maps_error_envelope_to_exception()
    {
        var client = CreateClient(_ => JsonResponse(HttpStatusCode.Conflict, """{"code":"CONFLICT","message":"Idempotency mismatch."}"""));

        Func<Task> act = () => client.GetRunAsync("run-1");

        var assertion = await act.Should().ThrowAsync<WebwrightException>();
        assertion.Which.Code.Should().Be("CONFLICT");
        assertion.Which.Message.Should().Be("Idempotency mismatch.");
        assertion.Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task GetArtifactAsync_truncates_text_artifacts()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("0123456789", Encoding.UTF8, "text/plain")
        });

        var artifact = await client.GetArtifactAsync("run-1", "log-1", maxBytes: 5);

        artifact.Truncated.Should().BeTrue();
        Encoding.UTF8.GetString(artifact.Bytes).Should().Be("01234");
    }

    [Fact]
    public async Task GetArtifactAsync_rejects_oversized_binary_artifacts()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3, 4, 5, 6])
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
            }
        });

        Func<Task> act = () => client.GetArtifactAsync("run-1", "screenshot-1", maxBytes: 5);

        var assertion = await act.Should().ThrowAsync<WebwrightException>();
        assertion.Which.Code.Should().Be("LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task WebwrightHealthProbe_returns_healthy_when_disabled()
    {
        var probe = CreateHealthProbe(
            browserEnabled: false,
            _ => throw new InvalidOperationException("HTTP should not be called."));

        var result = await probe.ProbeAsync();

        result.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task WebwrightHealthProbe_checks_authenticated_ready_endpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        var probe = CreateHealthProbe(
            browserEnabled: true,
            request =>
            {
                capturedRequest = request;
                return JsonResponse(HttpStatusCode.OK, """{"status":"ready"}""");
            });

        var result = await probe.ProbeAsync();

        result.IsHealthy.Should().BeTrue();
        capturedRequest!.RequestUri!.ToString().Should().Be("https://browser.test/ready");
    }

    [Fact]
    public async Task WebwrightHealthProbe_returns_unhealthy_for_failed_ready_endpoint()
    {
        var probe = CreateHealthProbe(
            browserEnabled: true,
            _ => JsonResponse(HttpStatusCode.ServiceUnavailable, """{"status":"down"}"""));

        var result = await probe.ProbeAsync();

        result.IsHealthy.Should().BeFalse();
    }

    private static WebwrightClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var services = new ServiceCollection();
        services
            .AddHttpClient(WebwrightClient.HttpClientName, client => client.BaseAddress = new Uri("https://browser.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(responseFactory));
        var provider = services.BuildServiceProvider();
        return new WebwrightClient(provider.GetRequiredService<IHttpClientFactory>());
    }

    private static WebwrightHealthProbe CreateHealthProbe(
        bool browserEnabled,
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var services = new ServiceCollection();
        services
            .AddHttpClient(WebwrightClient.HttpClientName, client => client.BaseAddress = new Uri("https://browser.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(responseFactory));
        var provider = services.BuildServiceProvider();
        var config = Options.Create(new LeanKernelConfig
        {
            Webwright = new WebwrightConfig
            {
                Enabled = browserEnabled,
                HealthProbe = new WebwrightHealthProbeConfig { Enabled = true },
                BaseUrl = "https://browser.test"
            }
        });
        return new WebwrightHealthProbe(
            provider.GetRequiredService<IHttpClientFactory>(),
            config,
            Mock.Of<ILogger<WebwrightHealthProbe>>());
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory = responseFactory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
