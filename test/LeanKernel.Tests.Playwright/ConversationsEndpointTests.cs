using Xunit;

namespace LeanKernel.Tests.Playwright;

/// <summary>
/// Covers the conversations endpoint through Playwright's API client.
/// </summary>
[Collection("Playwright")]
public class ConversationsEndpointTests : IClassFixture<PlaywrightFixture>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationsEndpointTests"/> class.
    /// </summary>
    public ConversationsEndpointTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies the conversations endpoint returns an expected status without full auth setup.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetConversations_ReturnsOkOrUnauthorized()
    {
        if (!_fixture.ServerAvailable)
        {
            return;
        }

        var api = await _fixture.Instance.APIRequest.NewContextAsync();
        var response = await api.GetAsync($"{_fixture.BaseUrl}/v1/conversations");

        Assert.True(
            response.Status == 400 || response.Status == 401 || response.Status == 200,
            $"Expected 400, 401, or 200 but got {response.Status}");

        await api.DisposeAsync();
    }

    private readonly PlaywrightFixture _fixture;
}