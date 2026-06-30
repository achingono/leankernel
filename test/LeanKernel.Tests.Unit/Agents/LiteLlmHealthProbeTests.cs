using System.Net;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Health;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Agents;

public class LiteLlmHealthProbeTests
{
    [Fact]
    public void ProviderName_returns_litellm()
    {
        var probe = CreateProbe();
        probe.ProviderName.Should().Be(ProviderNames.LiteLlm);
    }

    [Fact]
    public async Task ProbeAsync_returns_healthy_when_liveliness_succeeds()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = CreateProbe(handler);

        var result = await probe.ProbeAsync(default);

        result.IsHealthy.Should().BeTrue();
        result.Description.Should().Be("LiteLLM liveliness probe succeeded.");
    }

    [Fact]
    public async Task ProbeAsync_returns_healthy_when_fallback_health_succeeds()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("liveliness")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK));
        var probe = CreateProbe(handler);

        var result = await probe.ProbeAsync(default);

        result.IsHealthy.Should().BeTrue();
        result.Description.Should().Be("LiteLLM fallback health probe succeeded.");
    }

    [Fact]
    public async Task ProbeAsync_returns_unhealthy_when_both_fail()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("liveliness")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var probe = CreateProbe(handler);

        var result = await probe.ProbeAsync(default);

        result.IsHealthy.Should().BeFalse();
        result.Description.Should().Contain("404");
        result.Description.Should().Contain("503");
    }

    [Fact]
    public async Task ProbeAsync_returns_unhealthy_when_http_throws()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        var probe = CreateProbe(handler);

        var result = await probe.ProbeAsync(default);

        result.IsHealthy.Should().BeFalse();
        result.Description.Should().Be("LiteLLM health probe failed.");
        result.ErrorMessage.Should().Be("Connection refused");
    }

    private static LiteLlmHealthProbe CreateProbe(HttpMessageHandler? handler = null)
    {
        var client = new HttpClient(handler ?? new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))
        {
            BaseAddress = new Uri("http://localhost:4000")
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(LiteLlmHealthProbe.HttpClientName)).Returns(client);

        return new LiteLlmHealthProbe(
            factory.Object,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig { BaseUrl = "http://localhost:4000" }
            }),
            NullLogger<LiteLlmHealthProbe>.Instance);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
