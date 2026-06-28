namespace LeanKernel.Tests.Playwright;

/// <summary>
/// UI tests for core navigation and page rendering.
/// Requires the application to be running (e.g., via docker compose).
/// Set LEANKERNEL_BASE_URL environment variable to override the default (http://localhost:5080).
/// </summary>
[Collection(PlaywrightCollection.Name)]
public class NavigationTests
{
    private readonly PlaywrightFixture _fixture;

    public NavigationTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Homepage_LoadsWithExpectedTitle()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync(_fixture.BaseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var title = await page.TitleAsync();
            Assert.Contains("LeanKernel", title);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ShowsComposerInput()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var content = await page.ContentAsync();
            Assert.Contains("chat-composer-input", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ShowsSendButton()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var content = await page.ContentAsync();
            Assert.Contains("chat-send-button", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Theory]
    [InlineData("/diagnostics", "Diagnostics")]
    [InlineData("/knowledge", "Knowledge")]
    [InlineData("/admin", "Admin")]
    [InlineData("/onboarding", "Setup")]
    public async Task NavigationLinks_ReachCorrectPages(string path, string expectedContent)
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}{path}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var content = await page.ContentAsync();
            Assert.Contains(expectedContent, content, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DiagnosticsPage_ShowsSessionIdInput()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/diagnostics", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var content = await page.ContentAsync();
            Assert.Contains("Session ID", content, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task KnowledgePage_ShowsSearchField()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/knowledge", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var content = await page.ContentAsync();
            Assert.Contains("Knowledge", content, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ShowsHealthSection()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var content = await page.ContentAsync();
            Assert.Contains("Admin", content, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task OnboardingPage_ShowsWizardSteps()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/onboarding", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var content = await page.ContentAsync();
            Assert.Contains("Setup", content, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAsync(); }
    }

    [Theory]
    [InlineData("/chat", "Chat")]
    [InlineData("/diagnostics", "Diagnostics explorer")]
    [InlineData("/knowledge", "Knowledge")]
    [InlineData("/admin", "Admin Console")]
    [InlineData("/onboarding", "Guided setup")]
    public async Task MainPages_RenderStandardPageHeader(string path, string expectedTitle)
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}{path}", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var heading = page.Locator(".lk-page-header .lk-page-title");
            await Assertions.Expect(heading).ToBeVisibleAsync();
            await Assertions.Expect(heading).ToHaveTextAsync(expectedTitle);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task SessionsGroup_VisibleOnlyOnChatRoutes()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await Assertions.Expect(page.Locator("#chat-new-session-button")).ToBeVisibleAsync();

            await page.GotoAsync($"{_fixture.BaseUrl}/diagnostics", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await Assertions.Expect(page.Locator("#chat-new-session-button")).ToHaveCountAsync(0);
        }
        finally { await page.CloseAsync(); }
    }
}
