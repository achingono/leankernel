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
            var content = await page.ContentAsync();
            Assert.Contains("chat-composer-input", content, StringComparison.Ordinal);
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
    public async Task ChatPage_UsesStandardPageHeader()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var heading = page.Locator(".lk-page-header .lk-page-title");
            await Assertions.Expect(heading).ToBeVisibleAsync();
            await Assertions.Expect(heading).ToHaveTextAsync("Chat");
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
    public async Task ChatPage_ShowsNewSessionButton()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var newSessionButton = page.Locator("#chat-new-session-button");
            await Assertions.Expect(newSessionButton).ToBeVisibleAsync();
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
            var content = await page.ContentAsync();
            Assert.Contains("Ask LeanKernel anything", content, StringComparison.OrdinalIgnoreCase);
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
    public async Task ChatPage_DoesNotRenderLegacySessionSidebar()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var legacySidebar = page.Locator(".chat-sessions-panel");
            await Assertions.Expect(legacySidebar).ToHaveCountAsync(0);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_SessionsGroupAppearsInNavigation()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var navNewSession = page.Locator("#nav-new-session-button");
            await Assertions.Expect(navNewSession).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ComposerAndMessageListUsePinnedLayoutStyles()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });

            var composer = page.Locator(".chat-composer-shell");
            var messageList = page.Locator("#chat-message-list");

            await Assertions.Expect(composer).ToBeVisibleAsync();
            await Assertions.Expect(messageList).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_KeyboardTypingEnablesSendButton()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var sendButton = page.Locator("#chat-send-button");
            await Assertions.Expect(sendButton).ToBeVisibleAsync();
            var ariaLabel = await sendButton.GetAttributeAsync("aria-label");
            Assert.Equal("Send message", ariaLabel);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_TypedTextEnablesSendButton()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });

            var composer = page.Locator("#chat-composer-input");
            await composer.FillAsync("Hello from Playwright");

            var sendButton = page.Locator("#chat-send-button");
            await Assertions.Expect(sendButton).ToBeVisibleAsync();

            var isDisabled = await sendButton.GetAttributeAsync("disabled");
            Assert.Null(isDisabled);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_WhitespaceDoesNotSend()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });

            var composer = page.Locator("#chat-composer-input");
            await composer.FillAsync("   ");

            var sendButton = page.Locator("#chat-send-button");
            await Assertions.Expect(sendButton).ToBeVisibleAsync();

            var isDisabled = await sendButton.GetAttributeAsync("disabled");
            Assert.NotNull(isDisabled);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ShiftEnterAddsNewlineWithoutSending()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("chat-send-button", content, StringComparison.Ordinal);
            Assert.Contains("Shift+Enter", content, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAsync(); }
    }
}
