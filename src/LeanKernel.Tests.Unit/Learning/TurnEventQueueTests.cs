using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Learning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Learning;

public class TurnEventQueueTests
{
    [Fact]
    public async Task PublishAsync_drops_the_oldest_item_when_the_queue_is_full()
    {
        var queue = new TurnEventQueue(
            Options.Create(new LearningConfig { QueueCapacity = 2 }),
            NullLogger<TurnEventQueue>.Instance);

        await queue.PublishAsync(CreateTurnEvent("turn-1"));
        await queue.PublishAsync(CreateTurnEvent("turn-2"));
        await queue.PublishAsync(CreateTurnEvent("turn-3"));
        queue.Complete();

        var turns = new List<string>();
        while (await queue.WaitToReadAsync())
        {
            while (queue.TryRead(out var turnEvent))
            {
                turns.Add(turnEvent.TurnId);
            }
        }

        turns.Should().Equal("turn-2", "turn-3");
    }

    private static TurnEvent CreateTurnEvent(string turnId)
        => new()
        {
            SessionId = "session-1",
            TurnId = turnId,
            Role = "assistant",
            Content = $"Assistant response {turnId}",
            UserMessage = "User message",
            AssistantResponse = $"Assistant response {turnId}",
        };
}
