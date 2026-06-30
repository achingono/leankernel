using System.Net;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Knowledge.Health;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Knowledge;

public class GBrainHealthProbeTests
{
    [Fact]
    public void ProviderName_returns_gbrain()
    {
        var probe = CreateProbe();
        probe.ProviderName.Should().Be(ProviderNames.GBrain);
    }

    [Fact]
    public async Task ProbeAsync_returns_healthy_when_http_returns_success()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClientFactory = CreateHttpClientFactory(handler);
        var probe = CreateProbe(httpClientFactory);

        var result = await probe.ProbeAsync(default);

        result.IsHealthy.Should().BeTrue();
        result.Description.Should().Be("GBrain health probe succeeded.");
    }

    [Fact]
    public async Task ProbeAsync_returns_unhealthy_when_http_returns_non_success()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var httpClientFactory = CreateHttpClientFactory(handler);
        var probe = CreateProbe(httpClientFactory);

        var result = await probe.ProbeAsync(default);

        result.IsHealthy.Should().BeFalse();
        result.Description.Should().Contain("503");
    }

    [Fact]
    public async Task ProbeAsync_returns_unhealthy_when_http_throws()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        var httpClientFactory = CreateHttpClientFactory(handler);
        var probe = CreateProbe(httpClientFactory);

        var result = await probe.ProbeAsync(default);

        result.IsHealthy.Should().BeFalse();
        result.Description.Should().Be("GBrain health probe failed.");
        result.ErrorMessage.Should().Be("Connection refused");
    }

    private static GBrainHealthProbe CreateProbe(IHttpClientFactory? httpClientFactory = null)
        => new(
            httpClientFactory ?? Mock.Of<IHttpClientFactory>(),
            Options.Create(new GBrainConfig { BaseUrl = "http://localhost:5000" }),
            NullLogger<GBrainHealthProbe>.Instance);

    private static IHttpClientFactory CreateHttpClientFactory(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(GBrainHealthProbe.HttpClientName)).Returns(client);
        return factory.Object;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
