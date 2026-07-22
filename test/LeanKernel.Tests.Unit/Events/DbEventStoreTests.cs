using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Events;
using LeanKernel.Logic.Events;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace LeanKernel.Tests.Unit.Events;

public class DbEventStoreTests
{
    [Fact]
    public async Task AppendAsync_PersistsEvent()
    {
        var context = CreateContext();
        var store = new DbEventStore(context);

        var turnEvent = new TurnEvent
        {
            Envelope = CreateEnvelope("turn"),
            Role = "assistant",
            Content = "hello",
        };

        await store.AppendAsync(turnEvent);

        var persisted = await context.Events.SingleAsync();
        persisted.EventType.Should().Be("turn");
        persisted.RecordType.Should().Contain("TurnEvent");
        persisted.PayloadJson.Should().Contain("assistant");
    }

    [Fact]
    public async Task AppendBatchAsync_PersistsAllEvents()
    {
        var context = CreateContext();
        var store = new DbEventStore(context);

        var events = new object[]
        {
            new TurnEvent
            {
                Envelope = CreateEnvelope("turn"),
                Role = "user",
                Content = "first",
            },
            new TelemetryEvent
            {
                Envelope = CreateEnvelope("telemetry"),
                ServedModel = "gpt-4o",
            },
        };

        await store.AppendBatchAsync(events);

        var persisted = await context.Events
            .OrderBy(e => e.CreatedOn)
            .ToListAsync();

        persisted.Should().HaveCount(2);
        persisted.Select(e => e.EventType).Should().Contain(["turn", "telemetry"]);
    }

    [Fact]
    public async Task AppendBatchAsync_WithNoEvents_DoesNothing()
    {
        var context = CreateContext();
        var store = new DbEventStore(context);

        await store.AppendBatchAsync([]);

        (await context.Events.CountAsync()).Should().Be(0);
    }

    private static EntityContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new EntityContext(options);
    }

    private static EventEnvelope CreateEnvelope(string eventType) => new()
    {
        EventType = eventType,
        TenantId = Guid.NewGuid(),
        PersonId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        ChannelId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UtcNow,
    };
}
