using System.Net;
using System.Text.Json.Nodes;

namespace LeanKernel.Tests.Integration;

public class HealthCheckTests
{
    [Fact]
    public async Task Health_endpoint_reports_service_status()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", payload?["status"]?.GetValue<string>());
        Assert.Equal("ok", payload?["services"]?["runtime"]?.GetValue<string>());
        Assert.Equal("ok", payload?["services"]?["knowledge"]?.GetValue<string>());
    }
}
