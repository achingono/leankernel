using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LeanKernel.Tests.Integration;

/// <summary>
/// Covers the health endpoint exposed by the test host.
/// </summary>
public class HealthEndpointTests : IClassFixture<GatewayTestApplicationFactory>
{
    private readonly HttpClient _client;

    /// <summary>
    /// Creates a test instance backed by the shared gateway factory.
    /// </summary>
    public HealthEndpointTests(GatewayTestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Verifies the health endpoint returns success.
    /// </summary>
    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies the health payload reports a healthy state.
    /// </summary>
    [Fact]
    public async Task GetHealth_ReturnsHealthyStatus()
    {
        var response = await _client.GetAsync("/health");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().ContainEquivalentOf("healthy");
    }
}
