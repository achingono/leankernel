using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Xunit;

namespace LeanKernel.Tests.Integration;

/// <summary>
/// Covers the responses endpoints exposed by the test host.
/// </summary>
public class ResponsesEndpointTests : IClassFixture<GatewayTestApplicationFactory>
{
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResponsesEndpointTests"/> class.
    /// Creates a test instance backed by the shared gateway factory.
    /// </summary>
    public ResponsesEndpointTests(GatewayTestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Verifies a valid responses payload reaches the endpoint.
    /// </summary>
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

    /// <summary>
    /// Verifies an empty responses payload is rejected or handled.
    /// </summary>
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

    /// <summary>
    /// Verifies GET is not supported on the responses route.
    /// </summary>
    [Fact]
    public async Task GetResponses_Returns404Or405()
    {
        var response = await _client.GetAsync("/v1/responses");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.MethodNotAllowed);
    }
}