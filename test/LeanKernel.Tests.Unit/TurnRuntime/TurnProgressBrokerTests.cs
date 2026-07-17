using FluentAssertions;
using LeanKernel.Logic.TurnRuntime;
using Xunit;

namespace LeanKernel.Tests.Unit.TurnRuntime;

public sealed class TurnProgressBrokerTests
{
    [Fact]
    public async Task PublishAsync_DeliversUpdateToConversationSubscribers()
    {
        var broker = new TurnProgressBroker();
        var received = new List<TurnProgressUpdate>();

        using var subscription = broker.Subscribe("conv-1", update =>
        {
            received.Add(update);
            return Task.CompletedTask;
        });

        var updateMessage = new TurnProgressUpdate
        {
            ConversationId = "conv-1",
            Message = "Working",
            Stage = "search",
            PercentComplete = 25
        };

        await broker.PublishAsync("conv-1", updateMessage);

        received.Should().ContainSingle();
        received[0].Message.Should().Be("Working");
        received[0].Stage.Should().Be("search");
        received[0].PercentComplete.Should().Be(25);
    }

    [Fact]
    public async Task PublishAsync_IsolatesExceptionsAcrossSubscribers()
    {
        var broker = new TurnProgressBroker();
        var deliveredCount = 0;

        using var first = broker.Subscribe("conv-1", _ => throw new InvalidOperationException("boom"));
        using var second = broker.Subscribe("conv-1", _ =>
        {
            deliveredCount++;
            return Task.CompletedTask;
        });

        await broker.PublishAsync("conv-1", new TurnProgressUpdate
        {
            ConversationId = "conv-1",
            Message = "Still running"
        });

        deliveredCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_UnsubscribesHandler()
    {
        var broker = new TurnProgressBroker();
        var deliveredCount = 0;

        var subscription = broker.Subscribe("conv-1", _ =>
        {
            deliveredCount++;
            return Task.CompletedTask;
        });

        subscription.Dispose();
        subscription.Dispose();

        await broker.PublishAsync("conv-1", new TurnProgressUpdate
        {
            ConversationId = "conv-1",
            Message = "Should not deliver"
        });

        deliveredCount.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_UsesConversationIsolation()
    {
        var broker = new TurnProgressBroker();
        var firstConversation = 0;
        var secondConversation = 0;

        using var first = broker.Subscribe("conv-1", _ =>
        {
            firstConversation++;
            return Task.CompletedTask;
        });
        using var second = broker.Subscribe("conv-2", _ =>
        {
            secondConversation++;
            return Task.CompletedTask;
        });

        await broker.PublishAsync("conv-1", new TurnProgressUpdate { ConversationId = "conv-1", Message = "one" });
        await broker.PublishAsync("conv-2", new TurnProgressUpdate { ConversationId = "conv-2", Message = "two" });

        firstConversation.Should().Be(1);
        secondConversation.Should().Be(1);
    }
}
