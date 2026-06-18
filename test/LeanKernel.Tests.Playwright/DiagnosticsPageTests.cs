namespace LeanKernel.Tests.Playwright;

/// <summary>
/// UI tests for the Diagnostics explorer page.
/// Verifies session ID input, load button, empty state, and section rendering.
/// </summary>
[Collection(PlaywrightCollection.Name)]
public class DiagnosticsPageTests
{
    private readonly PlaywrightFixture _fixture;

    public DiagnosticsPageTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DiagnosticsPage_ShowsPageTitle()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/diagnostics", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var title = await page.TitleAsync();
            Assert.Contains("LeanKernel", title);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DiagnosticsPage_ShowsExplorerHeading()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/diagnostics", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Diagnostics explorer", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DiagnosticsPage_ShowsSessionIdInput()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/diagnostics", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Session ID", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DiagnosticsPage_ShowsLoadButton()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/diagnostics", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var loadButton = page.Locator("#diagnostics-load-button");
            await Assertions.Expect(loadButton).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DiagnosticsPage_LoadButtonDisabledWhenEmpty()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/diagnostics", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var loadButton = page.Locator("#diagnostics-load-button");
            var isDisabled = await loadButton.GetAttributeAsync("disabled");
            Assert.NotNull(isDisabled);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DiagnosticsPage_ShowsEmptyState()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/diagnostics", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Load a session to inspect turn diagnostics", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DiagnosticsPage_ShowsDescriptionText()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/diagnostics", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("context audit", content, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task DiagnosticsPage_ShowsLatestTurnNote()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/diagnostics", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("latest persisted turn", content, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAsync(); }
    }
}
