using FluentAssertions;

using LeanKernel.Events;
using LeanKernel.Logic.Events;

using Xunit;

namespace LeanKernel.Tests.Unit.Events;

public class EventCollectorTests
{
    private static readonly EventEnvelope TestEnvelope = new()
    {
        EventType = "test",
        TenantId = Guid.NewGuid(),
        PersonId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        ChannelId = Guid.NewGuid(),
    };

    [Fact]
    public void EmitTurn_AndConsumeAll_ReturnsTurnEvent()
    {
        var collector = new EventCollector();

        collector.EmitTurn(new TurnEvent
        {
            Envelope = TestEnvelope,
            Role = "user",
            Content = "Hello",
        });

        var events = collector.ConsumeAll();
        events.Should().HaveCount(1);
        events[0].Should().BeOfType<TurnEvent>();
        ((TurnEvent)events[0]).Content.Should().Be("Hello");
    }

    [Fact]
    public void EmitTelemetry_AndConsumeAll_ReturnsTelemetryEvent()
    {
        var collector = new EventCollector();

        collector.EmitTelemetry(new TelemetryEvent
        {
            Envelope = TestEnvelope,
            ServedModel = "gpt-4o",
            PromptTokens = 10,
        });

        var events = collector.ConsumeAll();
        events.Should().HaveCount(1);
        events[0].Should().BeOfType<TelemetryEvent>();
        ((TelemetryEvent)events[0]).ServedModel.Should().Be("gpt-4o");
    }

    [Fact]
    public void EmitToolCall_AndConsumeAll_ReturnsToolCallEvent()
    {
        var collector = new EventCollector();

        collector.EmitToolCall(new ToolCallEvent
        {
            Envelope = TestEnvelope,
            ToolName = "get_weather",
            Arguments = "{\"city\":\"London\"}",
        });

        var events = collector.ConsumeAll();
        events.Should().HaveCount(1);
        events[0].Should().BeOfType<ToolCallEvent>();
        ((ToolCallEvent)events[0]).ToolName.Should().Be("get_weather");
    }

    [Fact]
    public void ConsumeAll_ResetsQueue()
    {
        var collector = new EventCollector();

        collector.EmitTurn(new TurnEvent
        {
            Envelope = TestEnvelope,
            Role = "assistant",
            Content = "Hi",
        });

        var first = collector.ConsumeAll();
        var second = collector.ConsumeAll();

        first.Should().HaveCount(1);
        second.Should().HaveCount(0);
    }

    [Fact]
    public void MultipleEvents_AreReturnedInEmissionOrder()
    {
        var collector = new EventCollector();

        collector.EmitTurn(new TurnEvent { Envelope = TestEnvelope, Role = "user", Content = "first" });
        collector.EmitTurn(new TurnEvent { Envelope = TestEnvelope, Role = "user", Content = "second" });
        collector.EmitTurn(new TurnEvent { Envelope = TestEnvelope, Role = "user", Content = "third" });

        var events = collector.ConsumeAll();
        events.Should().HaveCount(3);
        ((TurnEvent)events[0]).Content.Should().Be("first");
        ((TurnEvent)events[1]).Content.Should().Be("second");
        ((TurnEvent)events[2]).Content.Should().Be("third");
    }
}