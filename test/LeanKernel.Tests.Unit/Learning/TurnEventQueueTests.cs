using FluentAssertions;

using LeanKernel.Events;
using LeanKernel.Services.Learning;

using Xunit;

namespace LeanKernel.Tests.Unit.Learning;

public sealed class TurnEventQueueTests
{
    [Fact]
    public async Task EnqueueThenDequeue_ReturnsSameEvent()
    {
        var queue = new TurnEventQueue(capacity: 4);
        var turn = CreateTurnEvent("first");

        await queue.EnqueueAsync(turn);
        var dequeued = await queue.TryDequeueAsync();

        dequeued.Should().NotBeNull();
        dequeued!.TurnId.Should().Be(turn.TurnId);
    }

    [Fact]
    public async Task FullQueue_DropsNewestWhenFull()
    {
        var queue = new TurnEventQueue(capacity: 1);
        var first = CreateTurnEvent("first");
        var second = CreateTurnEvent("second");

        await queue.EnqueueAsync(first);
        await queue.EnqueueAsync(second);
        var dequeued = await queue.TryDequeueAsync();

        dequeued.Should().NotBeNull();
        dequeued!.TurnId.Should().Be(first.TurnId);
        queue.DroppedCount.Should().Be(1);
    }

    private static TurnCompletedEvent CreateTurnEvent(string message)
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
            UserMessage = message,
            AssistantResponse = "ok",
        };
    }
}