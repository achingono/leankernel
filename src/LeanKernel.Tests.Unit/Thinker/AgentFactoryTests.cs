using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Thinker;

namespace LeanKernel.Tests.Unit.Thinker;

public class AgentFactoryTests
{
    [Fact]
    public void CreateAgent_ReturnsAgent_WithInstructions()
    {
        var mockClient = new TestChatClient();
        var factory = new AgentFactory(mockClient, NullLogger<AgentFactory>.Instance);

        var agent = factory.CreateAgent("You are a helpful assistant.");

        Assert.NotNull(agent);
    }

    [Fact]
    public void CreateAgent_WithTools_ReturnsAgent()
    {
        var mockClient = new TestChatClient();
        var factory = new AgentFactory(mockClient, NullLogger<AgentFactory>.Instance);

        var tool = AIFunctionFactory.Create(() => "result", "test_tool", "A test tool");
        var agent = factory.CreateAgent("Instructions", tools: [tool]);

        Assert.NotNull(agent);
    }

    [Fact]
    public void ChatClient_ExposesChatClient()
    {
        var mockClient = new TestChatClient();
        var factory = new AgentFactory(mockClient, NullLogger<AgentFactory>.Instance);

        Assert.Same(mockClient, factory.ChatClient);
    }

    /// <summary>
    /// Minimal test double for IChatClient — avoids external dependencies in unit tests.
    /// </summary>
    private sealed class TestChatClient : IChatClient
    {
        public void Dispose() { }

        public ChatClientMetadata Metadata => new();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IChatClient) ? this : null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "test response"));
            return Task.FromResult(response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
