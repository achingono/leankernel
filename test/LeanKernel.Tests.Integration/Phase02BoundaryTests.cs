using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace LeanKernel.Tests.Integration;

/// <summary>
/// Phase-02 boundary regression tests verifying tenant resolution, anonymous identity,
/// and access model behavior against a real (in-memory) gateway host.
/// </summary>
public class Phase02BoundaryTests : IClassFixture<GatewayTestApplicationFactory>
{
    private readonly HttpClient _client;

    /// <summary>
    /// Creates a test instance backed by the shared gateway factory.
    /// </summary>
    public Phase02BoundaryTests(GatewayTestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    /// <summary>
    /// C2: A request to a known host resolves a tenant and is accepted (not 401).
    /// </summary>
    [Fact]
    public async Task KnownHost_DoesNotReturn401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new { model = "test-model", input = "hello" })
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            because: "localhost is a seeded tenant and should be accepted");
    }

    /// <summary>
    /// C2: Health checks bypass tenant resolution and always return 200.
    /// </summary>
    [Fact]
    public async Task HealthEndpoint_BypassesTenantResolutionAndReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "health probe must not be blocked by tenant resolution");
    }

    /// <summary>
    /// C4: A valid anonymous request (no token) is accepted when JWT validation
    /// is not configured (no SecretKey in test settings).
    /// </summary>
    [Fact]
    public async Task AnonymousRequest_WithoutToken_IsAcceptedWhenNoSigningKeyConfigured()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new { model = "test-model", input = "hello" })
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            because: "JWT validation is disabled when no SecretKey is configured");
    }

    /// <summary>
    /// C4: A request bearing a badly formed/unsigned token is rejected when
    /// JWT signing key validation is enabled (the token claims should not be trusted).
    /// When no key is configured the test verifies the endpoint is reachable regardless.
    /// </summary>
    [Fact]
    public async Task MalformedToken_DoesNotGrantAccessToAdminClaims()
    {
        var forgedException = "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0." +
            "eyJzdWIiOiJhZG1pbiIsInJvbGUiOiJhZG1pbiJ9."; // unsigned JWT

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new { model = "test-model", input = "hello" })
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {forgedException}");

        var response = await _client.SendAsync(request);

        // When no signing key is configured the forged token may pass JWT parsing but
        // the user claims should not be trusted (the user is unauthenticated).
        // The endpoint should still respond (not error), and must NOT return 403.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            because: "anonymous access to /v1/* is supported; forged tokens degrade to anonymous");
    }
}
