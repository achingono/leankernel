using FluentAssertions;

using Xunit;

namespace LeanKernel.Tests.Integration;

public class DiagnosticsEndpointTests : IClassFixture<GatewayTestApplicationFactory>
{
    private readonly GatewayTestApplicationFactory _factory;

    public DiagnosticsEndpointTests(GatewayTestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDiagnosticsHealth_WithoutAuth_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/v1/diagnostics/health");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }
}