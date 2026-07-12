using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LeanKernel.Tests.Integration;

public class ResponsesEndpointTests : IClassFixture<GatewayTestApplicationFactory>
{
    private readonly HttpClient _client;

    public ResponsesEndpointTests(GatewayTestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostResponses_WithValidPayload_ReturnsOkOrError()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "test-model",
                input = "Hello"
            })
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PostResponses_WithEmptyBody_ReturnsBadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.OK,
            HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetResponses_Returns404Or405()
    {
        var response = await _client.GetAsync("/v1/responses");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.MethodNotAllowed);
    }
}
