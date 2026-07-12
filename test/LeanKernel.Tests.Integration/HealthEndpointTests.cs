using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LeanKernel.Tests.Integration;

public class HealthEndpointTests : IClassFixture<GatewayTestApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(GatewayTestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthyStatus()
    {
        var response = await _client.GetAsync("/health");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("healthy");
    }
}
