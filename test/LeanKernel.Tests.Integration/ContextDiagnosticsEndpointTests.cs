using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Tests.Integration;

public class ContextDiagnosticsEndpointTests
{
    [Fact]
    public async Task Context_endpoint_returns_the_latest_snapshot_when_authorized()
    {
        await using var factory = new GatewayTestApplicationFactory(apiKey: "secret-key");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var response = await client.GetAsync("/api/diagnostics/session-123/context");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ContextDiagnosticsResponse>();
        Assert.NotNull(payload);
        Assert.Equal("session-123", payload!.SessionId);
        Assert.Equal("turn-2", payload.TurnId);
        Assert.Equal(2, payload.TotalCandidatesConsidered);
        Assert.Equal(1, payload.TotalAdmitted);
    }

    [Fact]
    public async Task Budget_endpoint_supports_turn_id_filtering()
    {
        await using var factory = new GatewayTestApplicationFactory(apiKey: "secret-key");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var response = await client.GetAsync("/api/diagnostics/session-123/budget?turnId=turn-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BudgetDiagnosticsResponse>();
        Assert.NotNull(payload);
        Assert.Equal("turn-1", payload!.TurnId);
        Assert.Equal(128, payload.TotalBudgetTokens);
        Assert.Equal(96, payload.UsableBudgetTokens);
    }

    [Fact]
    public async Task History_endpoint_returns_404_when_no_snapshot_exists()
    {
        await using var factory = new GatewayTestApplicationFactory(apiKey: "secret-key");
        factory.ContextDiagnosticsService.Clear();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var response = await client.GetAsync("/api/diagnostics/session-123/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("History diagnostics not found for session 'session-123'.", payload?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task Context_diagnostics_endpoints_require_the_api_key_when_configured()
    {
        await using var factory = new GatewayTestApplicationFactory(apiKey: "secret-key");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/diagnostics/session-123/context");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
