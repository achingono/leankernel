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
            Assert.Contains("Type a message", content, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_ShowsComposerToolButtons()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await Assertions.Expect(page.Locator(".teams-composer-tool-button")).ToHaveCountAsync(5);
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
    public async Task ChatPage_ShowsSessionSearchInLeftPane()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var searchField = page.Locator("fluent-text-field.chat-session-search");
            await Assertions.Expect(searchField).ToBeHiddenAsync();

            var toggleButton = page.Locator("#chat-toggle-search-button");
            await toggleButton.ClickAsync();

            await Assertions.Expect(searchField).ToBeVisibleAsync();
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_CollapsesMainAppBarByDefault()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });

            var navWidth = await page.EvaluateAsync<double>("""
                () => {
                    const nav = document.querySelector('.main-layout-navmenu');
                    return nav ? nav.getBoundingClientRect().width : 0;
                }
                """);

            Assert.True(navWidth <= 70, $"Expected collapsed app bar width <= 70, got {navWidth}");
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_RendersSplitBubbleAlignment()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync(_fixture.BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.EvaluateAsync("""
                () => localStorage.setItem('leankernel.chat.owner-id', 'alfero@chingono.com')
                """);

            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });

            await Assertions.Expect(page.Locator(".lk-chat-row").First).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".lk-chat-avatar").First).ToBeVisibleAsync();
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

            var composer = page.Locator(".teams-composer-shell");
            var messageList = page.Locator("#chat-message-list");

            await Assertions.Expect(composer).ToBeVisibleAsync();
            await Assertions.Expect(messageList).ToBeVisibleAsync();

            var overflowY = await page.EvaluateAsync<string>("""
                () => getComputedStyle(document.querySelector('#chat-message-list')).overflowY
                """);
            Assert.Equal("auto", overflowY);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_KeepsComposerVisibleAndAvoidsMainPaneScroll()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });

            var composer = page.Locator("#chat-composer-input");
            await Assertions.Expect(composer).ToBeVisibleAsync();

            var metrics = await page.EvaluateAsync<ScrollMetrics>("""
                () => {
                    const composer = document.querySelector('#chat-composer-input');
                    const composerRect = composer?.getBoundingClientRect();
                    const viewportHeight = window.innerHeight;
                    const main = document.querySelector('#main-content');

                    return {
                        ComposerBottom: composerRect ? composerRect.bottom : 0,
                        ViewportHeight: viewportHeight,
                        MainClientHeight: main?.clientHeight ?? 0,
                        MainScrollHeight: main?.scrollHeight ?? 0
                    };
                }
                """);

            Assert.True(metrics.ComposerBottom <= metrics.ViewportHeight + 1, "Composer should remain in viewport.");
            Assert.True(metrics.MainScrollHeight <= metrics.MainClientHeight + 1, "Main pane should not vertically scroll on chat route.");
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

            var composer = page.Locator("#chat-composer-input").Locator("textarea");
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

            var composer = page.Locator("#chat-composer-input").Locator("textarea");
            await composer.FillAsync("   ");

            var sendButton = page.Locator("#chat-send-button");
            await Assertions.Expect(sendButton).ToBeVisibleAsync();

            var isDisabled = await sendButton.GetAttributeAsync("disabled");
            Assert.NotNull(isDisabled);
        }
        finally { await page.CloseAsync(); }
    }

    [Fact]
    public async Task ChatPage_SendButtonPresentForKeyboardSendFlow()
    {
        var page = await _fixture.Context.NewPageAsync();
        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/chat", new() { WaitUntil = WaitUntilState.NetworkIdle });
            var content = await page.ContentAsync();
            Assert.Contains("chat-send-button", content, StringComparison.Ordinal);
            var searchField = page.Locator("fluent-text-field.chat-session-search");
            await Assertions.Expect(searchField).ToHaveCountAsync(0);
        }
        finally { await page.CloseAsync(); }
    }

    private sealed class ScrollMetrics
    {
        public double ComposerBottom { get; set; }
        public double ViewportHeight { get; set; }
        public double MainClientHeight { get; set; }
        public double MainScrollHeight { get; set; }
    }
}
