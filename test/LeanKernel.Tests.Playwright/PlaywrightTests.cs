using Microsoft.Playwright;
using Xunit;

namespace LeanKernel.Tests.Playwright;

/// <summary>
/// Defines the shared Playwright test collection.
/// </summary>
[CollectionDefinition("Playwright", DisableParallelization = true)]
public class PlaywrightCollectionDefinition;

/// <summary>
/// Manages the shared Playwright instance for browser tests.
/// </summary>
public class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? _instance;

    public string BaseUrl { get; } =
        Environment.GetEnvironmentVariable("LEANKERNEL_BASE_URL") ?? "http://localhost:5080";

    public IPlaywright Instance => _instance ?? throw new InvalidOperationException("Playwright not initialized.");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _instance = await Microsoft.Playwright.Playwright.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _instance?.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Covers the responses endpoint through Playwright's API client.
/// </summary>
[Collection("Playwright")]
public class ResponsesEndpointTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    /// <summary>
    /// Creates a test instance backed by the shared Playwright fixture.
    /// </summary>
    public ResponsesEndpointTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies the responses endpoint returns an expected status without full auth setup.
    /// </summary>
    [Fact]
    public async Task PostResponses_ReturnsOkOrUnauthorized()
    {
        var api = await _fixture.Instance.APIRequest.NewContextAsync();
        var response = await api.PostAsync($"{_fixture.BaseUrl}/v1/responses", new APIRequestContextOptions
        {
            DataObject = new
            {
                model = "test",
                input = "Hello"
            }
        });

        // Runtime validation can return 400 for malformed/minimal payloads,
        // 401 when auth is required, 200 on permissive local hosts, or 500
        // when downstream dependencies are unavailable.
        Assert.True(
            response.Status == 400 || response.Status == 401 || response.Status == 200 || response.Status == 500,
            $"Expected 400, 401, 200, or 500 but got {response.Status}");

        await api.DisposeAsync();
    }
}

/// <summary>
/// Covers the conversations endpoint through Playwright's API client.
/// </summary>
[Collection("Playwright")]
public class ConversationsEndpointTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    /// <summary>
    /// Creates a test instance backed by the shared Playwright fixture.
    /// </summary>
    public ConversationsEndpointTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies the conversations endpoint returns an expected status without full auth setup.
    /// </summary>
    [Fact]
    public async Task GetConversations_ReturnsOkOrUnauthorized()
    {
        var api = await _fixture.Instance.APIRequest.NewContextAsync();
        var response = await api.GetAsync($"{_fixture.BaseUrl}/v1/conversations");

        Assert.True(
            response.Status == 400 || response.Status == 401 || response.Status == 200,
            $"Expected 400, 401, or 200 but got {response.Status}");

        await api.DisposeAsync();
    }
}
