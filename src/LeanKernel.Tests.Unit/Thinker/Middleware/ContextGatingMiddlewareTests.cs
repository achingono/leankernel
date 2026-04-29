using Microsoft.Extensions.AI;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Middleware;

namespace LeanKernel.Tests.Unit.Thinker.Middleware;

public class ContextGatingMiddlewareTests
{
    [Fact]
    public void PruneMessages_EmptyList_ReturnsEmpty()
    {
        var budget = ContextBudget.FromModelWindow(4000);
        var result = ContextGatingMiddleware.PruneMessages([], budget);

        Assert.Empty(result);
    }

    [Fact]
    public void PruneMessages_KeepsSystemMessage()
    {
        var budget = ContextBudget.FromModelWindow(4000);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are LeanKernel."),
            new(ChatRole.User, "Hello")
        };

        var result = ContextGatingMiddleware.PruneMessages(messages, budget);

        Assert.Equal(2, result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.Equal(ChatRole.User, result[1].Role);
    }

    [Fact]
    public void PruneMessages_KeepsLastUserMessage()
    {
        var budget = ContextBudget.FromModelWindow(4000);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "First"),
            new(ChatRole.Assistant, "Reply"),
            new(ChatRole.User, "Current query")
        };

        var result = ContextGatingMiddleware.PruneMessages(messages, budget);

        Assert.Equal("Current query", result[^1].Text);
    }

    [Fact]
    public void PruneMessages_TruncatesOldHistory_WhenBudgetExceeded()
    {
        // Very small budget — only room for recent messages
        var budget = new ContextBudget { TotalTokens = 50 }; // 50*0.40 = 20 token conversation budget

        var messages = new List<ChatMessage>();
        // Add many long messages that exceed budget
        for (int i = 0; i < 20; i++)
            messages.Add(new ChatMessage(
                i % 2 == 0 ? ChatRole.User : ChatRole.Assistant,
                new string('x', 200))); // ~50 tokens each

        var result = ContextGatingMiddleware.PruneMessages(messages, budget);

        // Should be significantly fewer than 20 messages
        Assert.True(result.Count < messages.Count,
            $"Expected pruning: got {result.Count} messages from {messages.Count}");
        // Last message should be preserved
        Assert.Equal(messages[^1].Text, result[^1].Text);
    }

    [Fact]
    public void PruneMessages_MaintainsChronologicalOrder()
    {
        var budget = ContextBudget.FromModelWindow(4000);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "msg1"),
            new(ChatRole.Assistant, "msg2"),
            new(ChatRole.User, "msg3"),
            new(ChatRole.Assistant, "msg4"),
            new(ChatRole.User, "msg5")
        };

        var result = ContextGatingMiddleware.PruneMessages(messages, budget);

        for (int i = 0; i < result.Count - 1; i++)
        {
            var currentIndex = messages.FindIndex(m => m.Text == result[i].Text);
            var nextIndex = messages.FindIndex(m => m.Text == result[i + 1].Text);
            Assert.True(currentIndex < nextIndex,
                $"Messages out of order: {result[i].Text} should come before {result[i + 1].Text}");
        }
    }

    [Fact]
    public void PruneMessages_SystemOnly_ReturnsSystemMessage()
    {
        var budget = ContextBudget.FromModelWindow(4000);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are LeanKernel.")
        };

        var result = ContextGatingMiddleware.PruneMessages(messages, budget);

        Assert.Single(result);
        Assert.Equal(ChatRole.System, result[0].Role);
    }

    [Fact]
    public void PruneMessages_MultipleSystemMessages_KeepsAll()
    {
        var budget = ContextBudget.FromModelWindow(4000);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "System 1"),
            new(ChatRole.System, "System 2"),
            new(ChatRole.User, "Hello")
        };

        var result = ContextGatingMiddleware.PruneMessages(messages, budget);

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.Count(m => m.Role == ChatRole.System));
    }

    [Fact]
    public void PruneMessages_SingleUserMessage_Preserved()
    {
        var budget = ContextBudget.FromModelWindow(4000);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Only message")
        };

        var result = ContextGatingMiddleware.PruneMessages(messages, budget);

        Assert.Single(result);
        Assert.Equal("Only message", result[0].Text);
    }

    [Fact]
    public void PruneMessages_ZeroBudget_KeepsOnlyLastMessage()
    {
        var budget = new ContextBudget { TotalTokens = 1 }; // minimal budget

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "old"),
            new(ChatRole.Assistant, "reply"),
            new(ChatRole.User, "current")
        };

        var result = ContextGatingMiddleware.PruneMessages(messages, budget);

        // Should at least keep the last message
        Assert.Contains(result, m => m.Text == "current");
    }

    [Fact]
    public async Task Wrap_PassesThroughToInnerClient()
    {
        var middleware = new ContextGatingMiddleware(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextGatingMiddleware>.Instance);
        var budget = ContextBudget.FromModelWindow(4000);

        var inner = new FakeGatingChatClient("response");
        var wrapped = middleware.Wrap(inner, budget);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are LeanKernel."),
            new(ChatRole.User, "Hello")
        };
        var result = await wrapped.GetResponseAsync(messages);

        Assert.Equal("response", result.Messages[0].Text);
    }

    [Fact]
    public void Wrap_ReturnsNonNullClient()
    {
        var middleware = new ContextGatingMiddleware(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextGatingMiddleware>.Instance);
        var budget = ContextBudget.FromModelWindow(4000);

        var inner = new FakeGatingChatClient("test");
        var wrapped = middleware.Wrap(inner, budget);

        Assert.NotNull(wrapped);
        Assert.NotSame(inner, wrapped);
    }

    private sealed class FakeGatingChatClient : IChatClient
    {
        private readonly string _response;
        public FakeGatingChatClient(string response) => _response = response;
        public void Dispose() { }
        public ChatClientMetadata Metadata => new();
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IChatClient) ? this : null;
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
