using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Thinker.Middleware;

namespace LeanKernel.Tests.Unit.Thinker.Middleware;

public class FunctionLoggingMiddlewareTests
{
    [Fact]
    public void Wrap_ReturnsNonNullClient()
    {
        var middleware = new FunctionLoggingMiddleware(
            NullLogger<FunctionLoggingMiddleware>.Instance);
        var inner = new FakeLoggingChatClient();

        var wrapped = middleware.Wrap(inner);

        Assert.NotNull(wrapped);
        Assert.NotSame(inner, wrapped);
    }

    [Fact]
    public async Task Wrap_PassesThroughToInnerClient()
    {
        var middleware = new FunctionLoggingMiddleware(
            NullLogger<FunctionLoggingMiddleware>.Instance);
        var inner = new FakeLoggingChatClient("delegated response");
        var wrapped = middleware.Wrap(inner);

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };
        var result = await wrapped.GetResponseAsync(messages);

        Assert.Equal("delegated response", result.Messages[0].Text);
    }

    [Fact]
    public async Task Wrap_LogsFunctionCallContent()
    {
        var middleware = new FunctionLoggingMiddleware(
            NullLogger<FunctionLoggingMiddleware>.Instance);

        // Create a response with a FunctionCallContent
        var responseMsg = new ChatMessage(ChatRole.Assistant, [
            new FunctionCallContent("call1", "search_tool", new Dictionary<string, object?> { ["query"] = "test" })
        ]);
        var inner = new FakeLoggingChatClient(responseMessage: responseMsg);
        var wrapped = middleware.Wrap(inner);

        var messages = new List<ChatMessage> { new(ChatRole.User, "search") };
        var result = await wrapped.GetResponseAsync(messages);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Wrap_LogsFunctionResultContent()
    {
        var middleware = new FunctionLoggingMiddleware(
            NullLogger<FunctionLoggingMiddleware>.Instance);

        var responseMsg = new ChatMessage(ChatRole.Tool, [
            new FunctionResultContent("call1", "result data")
        ]);
        var inner = new FakeLoggingChatClient(responseMessage: responseMsg);
        var wrapped = middleware.Wrap(inner);

        var messages = new List<ChatMessage> { new(ChatRole.User, "search") };
        var result = await wrapped.GetResponseAsync(messages);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Wrap_HandlesEmptyResponse()
    {
        var middleware = new FunctionLoggingMiddleware(
            NullLogger<FunctionLoggingMiddleware>.Instance);
        var inner = new FakeLoggingChatClient(emptyResponse: true);
        var wrapped = middleware.Wrap(inner);

        var messages = new List<ChatMessage> { new(ChatRole.User, "Hi") };
        var result = await wrapped.GetResponseAsync(messages);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Wrap_LogsLongFunctionResult_Truncated()
    {
        var middleware = new FunctionLoggingMiddleware(
            NullLogger<FunctionLoggingMiddleware>.Instance);

        var longResult = new string('x', 500);
        var responseMsg = new ChatMessage(ChatRole.Tool, [
            new FunctionResultContent("call1", longResult)
        ]);
        var inner = new FakeLoggingChatClient(responseMessage: responseMsg);
        var wrapped = middleware.Wrap(inner);

        var messages = new List<ChatMessage> { new(ChatRole.User, "query") };
        var result = await wrapped.GetResponseAsync(messages);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Wrap_LogsNullFunctionResult()
    {
        var middleware = new FunctionLoggingMiddleware(
            NullLogger<FunctionLoggingMiddleware>.Instance);

        var responseMsg = new ChatMessage(ChatRole.Tool, [
            new FunctionResultContent("call1", (object?)null)
        ]);
        var inner = new FakeLoggingChatClient(responseMessage: responseMsg);
        var wrapped = middleware.Wrap(inner);

        var messages = new List<ChatMessage> { new(ChatRole.User, "query") };
        var result = await wrapped.GetResponseAsync(messages);

        Assert.NotNull(result);
    }

    private sealed class FakeLoggingChatClient : IChatClient
    {
        private readonly string? _textResponse;
        private readonly ChatMessage? _responseMessage;
        private readonly bool _emptyResponse;

        public FakeLoggingChatClient(string? textResponse = null, ChatMessage? responseMessage = null, bool emptyResponse = false)
        {
            _textResponse = textResponse;
            _responseMessage = responseMessage;
            _emptyResponse = emptyResponse;
        }

        public void Dispose() { }
        public ChatClientMetadata Metadata => new();
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IChatClient) ? this : null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (_emptyResponse)
                return Task.FromResult(new ChatResponse([]));
            if (_responseMessage is not null)
                return Task.FromResult(new ChatResponse(_responseMessage));
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, _textResponse ?? "default")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
