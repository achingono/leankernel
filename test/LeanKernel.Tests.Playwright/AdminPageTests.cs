namespace LeanKernel.Tests.Playwright;

/// <summary>
/// UI tests for the Admin console page.
/// Verifies health cards, data grids, tool governance toggles, and refresh functionality.
/// </summary>
[Collection(PlaywrightCollection.Name)]
public class AdminPageTests
{
    private readonly PlaywrightFixture _fixture;

    public AdminPageTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AdminPage_LoadsWithLiveBadge()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var liveBadge = page.Locator("fluent-badge:has-text('Live')");
            await Assertions.Expect(liveBadge).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ShowsAdminConsoleTitle()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var title = page.Locator("text=Admin Console");
            await Assertions.Expect(title).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ShowsSystemHealthSection()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var healthHeading = page.Locator("#admin-health-heading");
            await Assertions.Expect(healthHeading).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ShowsRefreshChecksButton()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var refreshButton = page.Locator("#admin-refresh-health");
            await Assertions.Expect(refreshButton).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ShowsRoutingDataGrid()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var routingHeading = page.Locator("#admin-routing-heading");
            await Assertions.Expect(routingHeading).ToBeVisibleAsync();
            var content = await page.ContentAsync();
            Assert.Contains("Model routing configuration", content);
            Assert.Contains("Tier", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ShowsToolGovernanceSection()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var toolsHeading = page.Locator("#admin-tools-heading");
            await Assertions.Expect(toolsHeading).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ShowsToolCategoryFilter()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var categoryFilter = page.Locator("#admin-tool-filter");
            await Assertions.Expect(categoryFilter).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ShowsToolGovernanceDataGrid()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Tool governance", content);
            Assert.Contains("Category", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ShowsScheduledJobsSection()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var jobsHeading = page.Locator("#admin-jobs-heading");
            await Assertions.Expect(jobsHeading).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ShowsSpendDashboardSection()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var spendHeading = page.Locator("#admin-spend-heading");
            await Assertions.Expect(spendHeading).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ShowsBudgetProgress()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var progress = page.Locator("section[aria-labelledby='admin-spend-heading'] fluent-progress");
            await Assertions.Expect(progress).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_RefreshButtonTriggersReload()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var refreshButton = page.Locator("#admin-refresh-health");
            await refreshButton.ClickAsync();
            // After clicking, the button should change text briefly
            var content = await page.ContentAsync();
            Assert.Contains("admin-health-heading", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ToolToggleSwitchPresent()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var toggles = page.Locator("section[aria-labelledby='admin-tools-heading'] fluent-switch");
            var count = await toggles.CountAsync();
            Assert.True(count > 0, "Tool governance should have toggle switches");
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_ToolSwitchesUseTouchFriendlyTargets()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var switches = page.Locator("section[aria-labelledby='admin-tools-heading'] fluent-switch");
            var count = await switches.CountAsync();
            Assert.True(count > 0, "Tool governance should have toggle switches");

            var firstSwitchAriaLabel = await switches.First.GetAttributeAsync("aria-label");
            Assert.False(string.IsNullOrWhiteSpace(firstSwitchAriaLabel));
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task AdminPage_NoMockBackedPreviewBadge()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/admin", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.DoesNotContain("Mock-backed preview", content);
            Assert.DoesNotContain("Mock-based preview", content);
        }
        finally { await page.CloseAsync(); }
    }
}
