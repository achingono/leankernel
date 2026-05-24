namespace LeanKernel.Tests.Playwright;

/// <summary>
/// UI tests for the Chat page.
/// Verifies composer input, send button, session management, and empty state.
/// </summary>
[Collection(PlaywrightCollection.Name)]
public class ChatPageTests
{
    private readonly PlaywrightFixture _fixture;

    public ChatPageTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ChatPage_ShowsPageTitle()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var title = await page.TitleAsync();
            Assert.Contains("LeanKernel", title);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ShowsComposerTextArea()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var composer = page.Locator("#chat-composer-input");
            await Assertions.Expect(composer).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ShowsSendButton()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var sendButton = page.Locator("#chat-send-button");
            await Assertions.Expect(sendButton).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_SendButtonDisabledWhenEmpty()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var sendButton = page.Locator("#chat-send-button");
            var isDisabled = await sendButton.GetAttributeAsync("disabled");
            Assert.NotNull(isDisabled);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ShowsNewSessionButton()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("New session", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ShowsEmptyStateWhenNoMessages()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Start a LeanKernel conversation", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ShowsSessionListPanel()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Sessions", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ComposerHasPlaceholder()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var composer = page.Locator("#chat-composer-input");
            var placeholder = await composer.GetAttributeAsync("placeholder");
            Assert.Contains("LeanKernel", placeholder ?? string.Empty);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ShowsInputHintText()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("Enter to send", content);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ShowsChatHeading()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var heading = page.Locator("h2.chat-heading");
            await Assertions.Expect(heading).ToBeVisibleAsync();
            var text = await heading.TextContentAsync();
            Assert.Equal("Chat", text);
        }
        finally { await page.CloseAsync(); }
    }
}
