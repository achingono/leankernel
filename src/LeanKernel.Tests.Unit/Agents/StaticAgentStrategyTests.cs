using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using LeanKernel.Agents.Strategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Agents;

public class StaticAgentStrategyTests
{
    [Fact]
    public async Task InvokeAsync_maps_system_history_and_user_messages_before_calling_chat_client()
    {
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "hello from model")));
        var strategy = new StaticAgentStrategy(
            new AgentFactory(chatClient, NullLogger<AgentFactory>.Instance),
            NullLogger<StaticAgentStrategy>.Instance);

        var context = new AgentStrategyContext
        {
            SessionId = "session-1",
            TurnId = "turn-1",
            UserMessage = "Current question",
            SystemMessage = "System policy",
            History =
            [
                new ConversationTurn { Role = "user", Content = "Earlier user", Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:00Z") },
                new ConversationTurn { Role = "assistant", Content = "Earlier assistant", Timestamp = DateTimeOffset.Parse("2025-05-20T10:01:00Z") }
            ]
        };

        var response = await strategy.InvokeAsync(context);

        response.Should().Be("hello from model");
        context.ModelUsed.Should().Be("gpt-4o-mini");
        chatClient.ReceivedMessages.Should().HaveCount(4);
        chatClient.ReceivedMessages[0].Role.Should().Be(ChatRole.System);
        chatClient.ReceivedMessages[0].Text.Should().Be("System policy");
        chatClient.ReceivedMessages[1].Role.Should().Be(ChatRole.User);
        chatClient.ReceivedMessages[1].Text.Should().Be("Earlier user");
        chatClient.ReceivedMessages[1].CreatedAt.Should().Be(DateTimeOffset.Parse("2025-05-20T10:00:00Z"));
        chatClient.ReceivedMessages[2].Role.Should().Be(ChatRole.Assistant);
        chatClient.ReceivedMessages[2].Text.Should().Be("Earlier assistant");
        chatClient.ReceivedMessages[3].Role.Should().Be(ChatRole.User);
        chatClient.ReceivedMessages[3].Text.Should().Be("Current question");
        chatClient.ReceivedOptions.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_passes_tools_when_the_context_includes_them()
    {
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "tool-aware")));
        var strategy = new StaticAgentStrategy(
            new AgentFactory(chatClient, NullLogger<AgentFactory>.Instance),
            NullLogger<StaticAgentStrategy>.Instance);
        var tool = AIFunctionFactory.Create(
            () => "pong",
            new AIFunctionFactoryOptions { Name = "ping" });

        var response = await strategy.InvokeAsync(new AgentStrategyContext
        {
            SessionId = "session-2",
            TurnId = "turn-2",
            UserMessage = "Use a tool",
            SystemMessage = "System policy",
            History = [],
            Tools = [tool]
        });

        response.Should().Be("tool-aware");
        chatClient.ReceivedOptions.Should().NotBeNull();
        chatClient.ReceivedOptions!.Tools.Should().ContainSingle();
        chatClient.ReceivedOptions.Tools!.Single().Should().BeSameAs(tool);
    }

    private sealed class RecordingChatClient : IChatClient
    {
        private readonly ChatResponse _response;

        public RecordingChatClient(ChatResponse response)
        {
            _response = response;
        }

        public List<ChatMessage> ReceivedMessages { get; } = [];

        public ChatOptions? ReceivedOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ReceivedMessages.Clear();
            ReceivedMessages.AddRange(messages);
            ReceivedOptions = options;
            return Task.FromResult(_response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
