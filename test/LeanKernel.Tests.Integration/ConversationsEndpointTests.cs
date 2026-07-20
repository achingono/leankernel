using System.Net;

using FluentAssertions;

using Xunit;

namespace LeanKernel.Tests.Integration;

/// <summary>
/// Covers the conversations endpoints exposed by the test host.
/// </summary>
public class ConversationsEndpointTests : IClassFixture<GatewayTestApplicationFactory>
{
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationsEndpointTests"/> class.
    /// Creates a test instance backed by the shared gateway factory.
    /// </summary>
    public ConversationsEndpointTests(GatewayTestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Verifies listing conversations requires an agent identifier.
    /// </summary>
    [Fact]
    public async Task GetConversations_WithoutAgentId_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/v1/conversations");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies listing conversations reaches the endpoint for a valid query.
    /// </summary>
    [Fact]
    public async Task GetConversations_WithAgentId_ReturnsOkOrNotFound()
    {
        var response = await _client.GetAsync("/v1/conversations?agent_id=leankernel&model=test-model");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies fetching an unknown conversation returns an error status.
    /// </summary>
    [Fact]
    public async Task GetConversation_ByNonexistentId_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/v1/conversations/nonexistent-id");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }
}