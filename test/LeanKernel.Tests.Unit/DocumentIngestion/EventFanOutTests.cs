using FluentAssertions;

using LeanKernel.Events;
using LeanKernel.Logic.Events;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.DocumentIngestion;

public sealed class EventFanOutTests
{
    [Fact]
    public async Task PersistEventSubscriber_EmptyList_DoesNotCallStore()
    {
        var storeMock = new Mock<IEventStore>();
        var subscriber = new PersistEventSubscriber(storeMock.Object);

        await subscriber.HandleAsync(new List<object>());

        storeMock.Verify(s => s.AppendBatchAsync(It.IsAny<IEnumerable<object>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PersistEventSubscriber_WithEvents_CallsAppendBatch()
    {
        var storeMock = new Mock<IEventStore>();
        var subscriber = new PersistEventSubscriber(storeMock.Object);
        var events = new List<object> { new { Data = "test" } };

        await subscriber.HandleAsync(events);

        storeMock.Verify(s => s.AppendBatchAsync(events, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DocumentIngestionSubscriber_NoIngestionEvents_DoesNotEnqueue()
    {
        var queueMock = new Mock<IDocumentIngestionQueue>();
        var loggerMock = new Mock<ILogger<DocumentIngestionSubscriber>>();
        var scopeFactory = CreateScopeFactory(queueMock);
        var subscriber = new DocumentIngestionSubscriber(scopeFactory, loggerMock.Object);

        await subscriber.HandleAsync(new List<object> { new { SomeOther = "event" } });

        queueMock.Verify(q => q.EnqueueAsync(It.IsAny<DocumentIngestionJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DocumentIngestionSubscriber_WithIngestionEvent_EnqueuesJob()
    {
        var queueMock = new Mock<IDocumentIngestionQueue>();
        var loggerMock = new Mock<ILogger<DocumentIngestionSubscriber>>();
        var scopeFactory = CreateScopeFactory(queueMock);
        var subscriber = new DocumentIngestionSubscriber(scopeFactory, loggerMock.Object);
        var ev = new DocumentIngestionRequestedEvent
        {
            Envelope = new EventEnvelope { EventId = Guid.NewGuid(), TenantId = Guid.NewGuid(), ChannelId = Guid.NewGuid(), UserId = Guid.NewGuid(), CorrelationId = Guid.NewGuid().ToString() },
            StagedFilePath = "/tmp/staged/file.pdf",
            FileName = "file.pdf",
            ContentType = "application/pdf",
            AvailabilityScope = DocumentAvailabilityScope.Channel,
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
        };

        await subscriber.HandleAsync(new List<object> { ev });

        queueMock.Verify(q => q.EnqueueAsync(
            It.Is<DocumentIngestionJob>(j => j.FileName == "file.pdf" && j.Source == DocumentIngestionSource.ChannelAttachment),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DocumentIngestionSubscriber_QueueThrows_LogsButDoesNotThrow()
    {
        var queueMock = new Mock<IDocumentIngestionQueue>();
        queueMock
            .Setup(q => q.EnqueueAsync(It.IsAny<DocumentIngestionJob>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("queue full"));
        var loggerMock = new Mock<ILogger<DocumentIngestionSubscriber>>();
        var scopeFactory = CreateScopeFactory(queueMock);
        var subscriber = new DocumentIngestionSubscriber(scopeFactory, loggerMock.Object);
        var ev = new DocumentIngestionRequestedEvent
        {
            Envelope = new EventEnvelope { EventId = Guid.NewGuid(), TenantId = Guid.NewGuid(), ChannelId = Guid.NewGuid(), UserId = Guid.NewGuid(), CorrelationId = Guid.NewGuid().ToString() },
            StagedFilePath = "/tmp/staged/file.pdf",
            FileName = "file.pdf",
            ContentType = "application/pdf",
            AvailabilityScope = DocumentAvailabilityScope.Channel,
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
        };

        var act = async () => await subscriber.HandleAsync(new List<object> { ev });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DocumentIngestionSubscriber_EnqueuesAllMatchingEvents()
    {
        var queueMock = new Mock<IDocumentIngestionQueue>();
        var loggerMock = new Mock<ILogger<DocumentIngestionSubscriber>>();
        var scopeFactory = CreateScopeFactory(queueMock);
        var subscriber = new DocumentIngestionSubscriber(scopeFactory, loggerMock.Object);

        var createEvent = (string name) => new DocumentIngestionRequestedEvent
        {
            Envelope = new EventEnvelope { EventId = Guid.NewGuid(), TenantId = Guid.NewGuid(), ChannelId = Guid.NewGuid(), UserId = Guid.NewGuid(), CorrelationId = Guid.NewGuid().ToString() },
            StagedFilePath = $"/tmp/staged/{name}",
            FileName = name,
            ContentType = "text/plain",
            AvailabilityScope = DocumentAvailabilityScope.Channel,
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
        };

        await subscriber.HandleAsync(new List<object> { createEvent("a.txt"), createEvent("b.txt") });

        queueMock.Verify(q => q.EnqueueAsync(It.IsAny<DocumentIngestionJob>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static IServiceScopeFactory CreateScopeFactory(Mock<IDocumentIngestionQueue> queueMock)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => queueMock.Object);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
