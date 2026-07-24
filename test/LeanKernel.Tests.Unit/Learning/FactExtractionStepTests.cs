using System.Collections.Generic;
using System.Linq;

using FluentAssertions;

using LeanKernel;
using LeanKernel.Events;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
using LeanKernel.Services.Learning.Steps;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace LeanKernel.Tests.Unit.Learning;

public sealed class FactExtractionStepTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsExtractedFacts()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new FactExtractionService(
            new StaticChatClient("[\"Budget approved\"]"),
            Options.Create(new FactExtractionSettings()),
            new MemoryPageRenderer()));

        var provider = services.BuildServiceProvider();
        var step = new FactExtractionStep(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<FactExtractionStep>.Instance);

        var facts = await step.ExecuteAsync(CreateTurnEvent());

        facts.Should().ContainSingle().Which.Should().Be("Budget approved");
    }

    private static TurnCompletedEvent CreateTurnEvent()
    {
        return new TurnCompletedEvent
        {
            Envelope = new EventEnvelope
            {
                EventType = "turn_completed",
                TenantId = Guid.NewGuid(),
                PersonId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                ChannelId = Guid.NewGuid(),
            },
            TurnId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            UserMessage = "Can you remember this budget note?",
            AssistantResponse = "Sure, I can remember it.",
        };
    }

    private sealed class StaticChatClient(string text) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return AsyncEnumerable.Empty<ChatResponseUpdate>();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }
}
