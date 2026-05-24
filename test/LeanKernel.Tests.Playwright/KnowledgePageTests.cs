namespace LeanKernel.Tests.Playwright;

/// <summary>
/// UI tests for the Knowledge page.
/// Verifies search input, browse list, page detail panel, and create button.
/// </summary>
[Collection(PlaywrightCollection.Name)]
public class KnowledgePageTests
{
    private readonly PlaywrightFixture _fixture;

    public KnowledgePageTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task KnowledgePage_ShowsPageTitle()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/knowledge", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var title = await page.TitleAsync();
            Assert.Contains("Knowledge", title);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task KnowledgePage_ShowsKnowledgeHeading()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/knowledge", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Knowledge", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task KnowledgePage_ShowsSearchInput()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/knowledge", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var searchInput = page.Locator("#knowledge-search-input");
            await Assertions.Expect(searchInput).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task KnowledgePage_ShowsCreateNewPageButton()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/knowledge", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var createButton = page.Locator("fluent-button:has-text('Create new page')");
            await Assertions.Expect(createButton).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task KnowledgePage_ShowsBrowsePagesSection()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/knowledge", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var browseTitle = page.Locator("#knowledge-browse-title");
            await Assertions.Expect(browseTitle).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task KnowledgePage_ShowsSortSelect()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/knowledge", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var sortSelect = page.Locator("#knowledge-sort-select");
            await Assertions.Expect(sortSelect).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task KnowledgePage_ShowsSearchSection()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/knowledge", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var searchTitle = page.Locator("#knowledge-search-title");
            await Assertions.Expect(searchTitle).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task KnowledgePage_ShowsEmptySearchPrompt()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/knowledge", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Start with a question", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task KnowledgePage_ShowsPageDetailEmptyState()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/knowledge", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Select a page", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task KnowledgePage_ShowsPaginationButtons()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/knowledge", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Previous", content);
            Assert.Contains("Next", content);
        }
        finally { await page.CloseAsync(); }
    }
}
