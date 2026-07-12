using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LeanKernel.Tests.Integration;

public class ConversationsEndpointTests : IClassFixture<GatewayTestApplicationFactory>
{
    private readonly HttpClient _client;

    public ConversationsEndpointTests(GatewayTestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetConversations_WithoutAgentId_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/v1/conversations");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetConversations_WithAgentId_ReturnsOkOrNotFound()
    {
        var response = await _client.GetAsync("/v1/conversations?agent_id=leankernel&model=test-model");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetConversation_ByNonexistentId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/v1/conversations/nonexistent-id");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }
}
