using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LeanKernel.Tests.Integration;

/// <summary>
/// Tests that verify endpoint availability and response behavior.
/// MAF endpoints mirror the OpenAI public API and do not require JWT auth.
/// </summary>
public class AuthenticationEndpointTests : IClassFixture<GatewayTestApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthenticationEndpointTests(GatewayTestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_IsPublicAndReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConversationsEndpoint_IsReachable()
    {
        var response = await _client.GetAsync("/v1/conversations");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ResponsesEndpoint_IsReachable()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [InlineData("/v1/conversations", "DELETE")]
    [InlineData("/v1/conversations", "PUT")]
    [InlineData("/v1/conversations", "PATCH")]
    public async Task ConversationsEndpoint_UnsupportedMethods_ReturnsMethodNotAllowed(string url, string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.MethodNotAllowed,
            HttpStatusCode.NotFound);
    }
}
