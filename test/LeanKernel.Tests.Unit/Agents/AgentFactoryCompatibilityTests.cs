using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LeanKernel.Tests.Unit.Agents;

public class AgentFactoryCompatibilityTests
{
    [Fact]
    public async Task ChatClient_executes_legacy_function_payloads_and_replays_the_model()
    {
        var toolExecutor = new Mock<IToolExecutor>(MockBehavior.Strict);
        toolExecutor
            .Setup(executor => executor.ExecuteAsync(
                "wiki_write",
                It.Is<IDictionary<string, object?>>(arguments => MatchesWikiWriteArguments(arguments)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                ToolName = "wiki_write",
                Success = true,
                Output = "Page 'greeting' updated successfully."
            });

        const string legacyJson = "{\"type\":\"function\",\"name\":\"wiki_write\",\"parameters\":{\"key\":\"greeting\",\"content\":\"Hello!\"}}";

        var chatClient = new SequencedChatClient([
            new ChatResponse(new ChatMessage(ChatRole.Assistant, legacyJson)),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Page 'greeting' updated successfully."))
        ]);

        var factory = new AgentFactory(
            chatClient,
            NullLogger<AgentFactory>.Instance,
            toolExecutor: toolExecutor.Object);

        var response = await factory.ChatClient.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Hello!")
        ]);

        response.Text.Should().Be("Page 'greeting' updated successfully.");
        chatClient.CallCount.Should().Be(2);
        chatClient.ReceivedMessages[1].Should().HaveCount(3);
        chatClient.ReceivedMessages[1][1].Role.Should().Be(ChatRole.Assistant);
        chatClient.ReceivedMessages[1][1].Contents.Should().ContainSingle()
            .Which.Should().BeOfType<FunctionCallContent>();
        chatClient.ReceivedMessages[1][2].Role.Should().Be(ChatRole.Tool);
        chatClient.ReceivedMessages[1][2].Contents.Should().ContainSingle()
            .Which.Should().BeOfType<FunctionResultContent>();
        toolExecutor.VerifyAll();
    }

    [Fact]
    public async Task ChatClient_leaves_unknown_legacy_tools_untouched()
    {
        var toolExecutor = new Mock<IToolExecutor>(MockBehavior.Strict);
        toolExecutor
            .Setup(executor => executor.ExecuteAsync(
                "missing_tool",
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                ToolName = "missing_tool",
                Success = false,
                Error = "Tool 'missing_tool' not found"
            });

        const string legacyJson = "{\"type\":\"function\",\"name\":\"missing_tool\",\"parameters\":{\"value\":\"x\"}}";
        var chatClient = new SequencedChatClient([
            new ChatResponse(new ChatMessage(ChatRole.Assistant, legacyJson))
        ]);

        var factory = new AgentFactory(
            chatClient,
            NullLogger<AgentFactory>.Instance,
            toolExecutor: toolExecutor.Object);

        var response = await factory.ChatClient.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Hello!")
        ]);

        response.Text.Should().Be(legacyJson);
        chatClient.CallCount.Should().Be(1);
        toolExecutor.VerifyAll();
    }

    [Fact]
    public async Task ChatClient_leaves_non_legacy_payloads_untouched()
    {
        var toolExecutor = new Mock<IToolExecutor>(MockBehavior.Strict);
        const string malformedJson = "{\"type\":\"function\",\"name\":\"wiki_write\",\"parameters\":{\"key\":\"greeting\"},\"extra\":true}";

        var chatClient = new SequencedChatClient([
            new ChatResponse(new ChatMessage(ChatRole.Assistant, malformedJson))
        ]);

        var factory = new AgentFactory(
            chatClient,
            NullLogger<AgentFactory>.Instance,
            toolExecutor: toolExecutor.Object);

        var response = await factory.ChatClient.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Hello!")
        ]);

        response.Text.Should().Be(malformedJson);
        chatClient.CallCount.Should().Be(1);
        toolExecutor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChatClient_executes_fenced_legacy_function_payloads()
    {
        var toolExecutor = new Mock<IToolExecutor>(MockBehavior.Strict);
        toolExecutor
            .Setup(executor => executor.ExecuteAsync(
                "wiki_write",
                It.Is<IDictionary<string, object?>>(arguments => MatchesWikiWriteArguments(arguments)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                ToolName = "wiki_write",
                Success = true,
                Output = "Page 'greeting' updated successfully."
            });

        const string legacyJson = "```json\n{\"type\":\"function\",\"name\":\"wiki_write\",\"parameters\":{\"key\":\"greeting\",\"content\":\"Hello!\"}}\n```";

        var chatClient = new SequencedChatClient([
            new ChatResponse(new ChatMessage(ChatRole.Assistant, legacyJson)),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "Page 'greeting' updated successfully."))
        ]);

        var factory = new AgentFactory(
            chatClient,
            NullLogger<AgentFactory>.Instance,
            toolExecutor: toolExecutor.Object);

        var response = await factory.ChatClient.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Hello!")
        ]);

        response.Text.Should().Be("Page 'greeting' updated successfully.");
        chatClient.CallCount.Should().Be(2);
        toolExecutor.VerifyAll();
    }

    [Fact]
    public async Task ChatClient_returns_tool_output_when_replay_keeps_returning_legacy_payload()
    {
        var toolExecutor = new Mock<IToolExecutor>(MockBehavior.Strict);
        toolExecutor
            .Setup(executor => executor.ExecuteAsync(
                "wiki_write",
                It.Is<IDictionary<string, object?>>(arguments => MatchesWikiWriteArguments(arguments)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                ToolName = "wiki_write",
                Success = true,
                Output = "Page 'greeting' updated successfully."
            });

        const string legacyJson = "{\"type\":\"function\",\"name\":\"wiki_write\",\"parameters\":{\"key\":\"greeting\",\"content\":\"Hello!\"}}";
        var chatClient = new SequencedChatClient([
            new ChatResponse(new ChatMessage(ChatRole.Assistant, legacyJson)),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, legacyJson)),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, legacyJson))
        ]);

        var factory = new AgentFactory(
            chatClient,
            NullLogger<AgentFactory>.Instance,
            toolExecutor: toolExecutor.Object);

        var response = await factory.ChatClient.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Hello!")
        ]);

        response.Text.Should().Be("Page 'greeting' updated successfully.");
        chatClient.CallCount.Should().Be(2);
        toolExecutor.VerifyAll();
    }

    [Fact]
    public async Task ChatClient_converts_nested_legacy_parameters_before_tool_execution()
    {
        var toolExecutor = new Mock<IToolExecutor>(MockBehavior.Strict);
        toolExecutor
            .Setup(executor => executor.ExecuteAsync(
                "wiki_write",
                It.Is<IDictionary<string, object?>>(arguments => MatchesComplexArguments(arguments)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolResult
            {
                ToolName = "wiki_write",
                Success = true,
                Output = "ok"
            });

        const string legacyJson = "{\"type\":\"function\",\"name\":\"wiki_write\",\"parameters\":{\"key\":\"greeting\",\"content\":\"Hello!\",\"count\":2,\"decimal\":2.5,\"enabled\":true,\"is_archived\":false,\"optional\":null,\"float\":1e40,\"tags\":[\"a\",\"b\"],\"nested\":{\"note\":\"x\"}}}";
        var chatClient = new SequencedChatClient([
            new ChatResponse(new ChatMessage(ChatRole.Assistant, legacyJson)),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
        ]);

        var factory = new AgentFactory(
            chatClient,
            NullLogger<AgentFactory>.Instance,
            toolExecutor: toolExecutor.Object);

        var response = await factory.ChatClient.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Hello!")
        ]);

        response.Text.Should().Be("ok");
        toolExecutor.VerifyAll();
    }

    [Fact]
    public async Task ChatClient_leaves_empty_responses_untouched()
    {
        var toolExecutor = new Mock<IToolExecutor>(MockBehavior.Strict);
        var chatClient = new SequencedChatClient([
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "   "))
        ]);

        var factory = new AgentFactory(
            chatClient,
            NullLogger<AgentFactory>.Instance,
            toolExecutor: toolExecutor.Object);

        var response = await factory.ChatClient.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Hello!")
        ]);

        response.Text.Should().Be("   ");
        toolExecutor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"type\":\"function\",\"name\":\"wiki_write\"}")]
    [InlineData("{\"type\":\"tool\",\"name\":\"wiki_write\",\"parameters\":{}}")]
    [InlineData("{\"type\":\"function\",\"name\":123,\"parameters\":{}}")]
    [InlineData("{\"type\":\"function\",\"name\":\"wiki_write\",\"parameters\":[]}")]
    [InlineData("```json\n{invalid-json}\n```")]
    public async Task ChatClient_rejects_invalid_legacy_payload_shapes(string payload)
    {
        var toolExecutor = new Mock<IToolExecutor>(MockBehavior.Strict);
        var chatClient = new SequencedChatClient([
            new ChatResponse(new ChatMessage(ChatRole.Assistant, payload))
        ]);

        var factory = new AgentFactory(
            chatClient,
            NullLogger<AgentFactory>.Instance,
            toolExecutor: toolExecutor.Object);

        var response = await factory.ChatClient.GetResponseAsync([
            new ChatMessage(ChatRole.User, "Hello!")
        ]);

        response.Text.Should().Be(payload);
        toolExecutor.VerifyNoOtherCalls();
    }

    [Fact]
    public void ChatClient_forwards_service_queries_and_disposal()
    {
        var toolExecutor = new Mock<IToolExecutor>(MockBehavior.Strict);
        var chatClient = new SequencedChatClient([
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
        ])
        {
            ServiceInstance = "service-instance"
        };

        var factory = new AgentFactory(
            chatClient,
            NullLogger<AgentFactory>.Instance,
            toolExecutor: toolExecutor.Object);

        factory.ChatClient.GetService(typeof(string)).Should().Be("service-instance");
        factory.ChatClient.Dispose();
        chatClient.WasDisposed.Should().BeTrue();
    }

    private static bool MatchesWikiWriteArguments(IDictionary<string, object?> arguments)
    {
        var key = arguments.TryGetValue("key", out var keyValue) ? keyValue?.ToString() : null;
        var content = arguments.TryGetValue("content", out var contentValue) ? contentValue?.ToString() : null;
        return string.Equals(key, "greeting", StringComparison.Ordinal)
            && string.Equals(content, "Hello!", StringComparison.Ordinal);
    }

    private static bool MatchesComplexArguments(IDictionary<string, object?> arguments)
    {
        var key = arguments.TryGetValue("key", out var keyValue) ? keyValue?.ToString() : null;
        var content = arguments.TryGetValue("content", out var contentValue) ? contentValue?.ToString() : null;
        var count = arguments.TryGetValue("count", out var countValue) ? countValue : null;
        var decimalValue = arguments.TryGetValue("decimal", out var decimalObject) ? decimalObject : null;
        var enabled = arguments.TryGetValue("enabled", out var enabledValue) ? enabledValue : null;
        var isArchived = arguments.TryGetValue("is_archived", out var archivedValue) ? archivedValue : null;
        var optional = arguments.TryGetValue("optional", out var optionalValue) ? optionalValue : new object();
        var floatValue = arguments.TryGetValue("float", out var floatObject) ? floatObject : null;
        var tags = arguments.TryGetValue("tags", out var tagsValue) ? tagsValue : null;

        var optionalIsNullOrStringNull = optional is null
            || (optional is string optionalString && optionalString == "null");

        return string.Equals(key, "greeting", StringComparison.Ordinal)
            && string.Equals(content, "Hello!", StringComparison.Ordinal)
            && count is long
            && decimalValue is decimal
            && enabled is bool boolValue && boolValue
            && isArchived is bool archived && !archived
            && optionalIsNullOrStringNull
            && floatValue is double
            && tags is object[];
    }

    private sealed class SequencedChatClient(IReadOnlyList<ChatResponse> responses) : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new(responses);

        public List<IReadOnlyList<ChatMessage>> ReceivedMessages { get; } = [];

        public int CallCount => ReceivedMessages.Count;

        public object? ServiceInstance { get; init; }

        public bool WasDisposed { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var messageList = messages.ToList();
            ReceivedMessages.Add(messageList);

            var response = _responses.Count > 1
                ? _responses.Dequeue()
                : _responses.Peek();

            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => ServiceInstance;

        public void Dispose()
        {
            WasDisposed = true;
        }
    }
}
