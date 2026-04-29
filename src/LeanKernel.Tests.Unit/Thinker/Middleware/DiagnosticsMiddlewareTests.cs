using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Thinker.Middleware;

namespace LeanKernel.Tests.Unit.Thinker.Middleware;

public class DiagnosticsMiddlewareTests
{
    [Fact]
    public void Wrap_ReturnsNonNullAgent()
    {
        var middleware = new DiagnosticsMiddleware(
            NullLogger<DiagnosticsMiddleware>.Instance);

        var mockClient = new TestChatClient();
        var agent = new ChatClientAgent(mockClient, instructions: "test");
        var wrapped = middleware.Wrap(agent);

        Assert.NotNull(wrapped);
    }

    [Fact]
    public void Wrap_ReturnsDifferentInstance()
    {
        var middleware = new DiagnosticsMiddleware(
            NullLogger<DiagnosticsMiddleware>.Instance);

        var mockClient = new TestChatClient();
        var agent = new ChatClientAgent(mockClient, instructions: "test");
        var wrapped = middleware.Wrap(agent);

        // Wrapped should be a different (middleware-wrapped) agent
        Assert.NotSame(agent, wrapped);
    }

    private sealed class TestChatClient : IChatClient
    {
        public void Dispose() { }
        public ChatClientMetadata Metadata => new();
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IChatClient) ? this : null;
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "test")));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
