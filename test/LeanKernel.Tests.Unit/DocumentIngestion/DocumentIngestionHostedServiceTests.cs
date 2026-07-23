using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.DocumentIngestion;

public sealed class DocumentIngestionHostedServiceTests
{
    private static readonly IOptions<DocumentIngestionToolSettings> DefaultSettings =
        Options.Create(new DocumentIngestionToolSettings { EnqueueTimeoutSeconds = 300 });
    [Fact]
    public async Task ExecuteAsync_RecoversStaleLeasesOnStart()
    {
        var queueMock = new Mock<IDocumentIngestionQueue>();
        var libraryMock = new Mock<IDocumentLibraryService>();
        var loggerMock = new Mock<ILogger<DocumentIngestionHostedService>>();

        queueMock
            .Setup(q => q.TryClaimNextAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentIngestionJobEntity?)null);

        var scopeFactory = CreateScopeFactory(queueMock, libraryMock);
        var service = new DocumentIngestionHostedService(scopeFactory, DefaultSettings, loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await service.StartAsync(cts.Token);
            await service.ExecuteTask!;
        }
        catch (OperationCanceledException)
        {
        }

        queueMock.Verify(q => q.RecoverStaleLeasesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ClaimsAndProcessesJob()
    {
        var queueMock = new Mock<IDocumentIngestionQueue>();
        var libraryMock = new Mock<IDocumentLibraryService>();
        var loggerMock = new Mock<ILogger<DocumentIngestionHostedService>>();

        var jobEntity = new DocumentIngestionJobEntity
        {
            Id = Guid.NewGuid(),
            Status = "Processing",
            FilePath = "/tmp/test.txt",
            FileName = "test.txt",
            ContentType = "text/plain",
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            AvailabilityScope = "User",
            Source = "Upload",
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow,
        };

        queueMock
            .Setup(q => q.TryClaimNextAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobEntity);

        queueMock
            .Setup(q => q.CompleteAsync(It.IsAny<Guid>(), It.IsAny<IngestionResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        libraryMock
            .Setup(l => l.IngestDocumentAsync(It.IsAny<DocumentIngestionJob>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionResult("abc123", true, false));

        var scopeFactory = CreateScopeFactory(queueMock, libraryMock);
        var service = new DocumentIngestionHostedService(scopeFactory, DefaultSettings, loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await service.StartAsync(cts.Token);
            await service.ExecuteTask!;
        }
        catch (OperationCanceledException)
        {
        }

        libraryMock.Verify(l => l.IngestDocumentAsync(It.IsAny<DocumentIngestionJob>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        queueMock.Verify(q => q.CompleteAsync(jobEntity.Id, It.IsAny<IngestionResult>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenIngestionFails_FailsJob()
    {
        var queueMock = new Mock<IDocumentIngestionQueue>();
        var libraryMock = new Mock<IDocumentLibraryService>();
        var loggerMock = new Mock<ILogger<DocumentIngestionHostedService>>();

        var jobEntity = new DocumentIngestionJobEntity
        {
            Id = Guid.NewGuid(),
            Status = "Processing",
            FilePath = "/tmp/bad.txt",
            FileName = "bad.txt",
            ContentType = "text/plain",
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            AvailabilityScope = "User",
            Source = "Upload",
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow,
        };

        queueMock
            .Setup(q => q.TryClaimNextAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobEntity);

        libraryMock
            .Setup(l => l.IngestDocumentAsync(It.IsAny<DocumentIngestionJob>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ingestion failed"));

        var scopeFactory = CreateScopeFactory(queueMock, libraryMock);
        var service = new DocumentIngestionHostedService(scopeFactory, DefaultSettings, loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await service.StartAsync(cts.Token);
            await service.ExecuteTask!;
        }
        catch (OperationCanceledException)
        {
        }

        queueMock.Verify(q => q.FailAsync(
            jobEntity.Id,
            It.IsAny<string>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenQueueThrows_ContinuesLoop()
    {
        var queueMock = new Mock<IDocumentIngestionQueue>();
        var libraryMock = new Mock<IDocumentLibraryService>();
        var loggerMock = new Mock<ILogger<DocumentIngestionHostedService>>();

        queueMock
            .Setup(q => q.TryClaimNextAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db error"));

        var scopeFactory = CreateScopeFactory(queueMock, libraryMock);
        var service = new DocumentIngestionHostedService(scopeFactory, DefaultSettings, loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await service.StartAsync(cts.Token);
            await service.ExecuteTask!;
        }
        catch (OperationCanceledException)
        {
        }

        // Service should not throw despite queue errors
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteAsync_RecoverFails_Continues()
    {
        var queueMock = new Mock<IDocumentIngestionQueue>();
        var libraryMock = new Mock<IDocumentLibraryService>();
        var loggerMock = new Mock<ILogger<DocumentIngestionHostedService>>();

        queueMock
            .Setup(q => q.RecoverStaleLeasesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("recover failure"));

        queueMock
            .Setup(q => q.TryClaimNextAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentIngestionJobEntity?)null);

        var scopeFactory = CreateScopeFactory(queueMock, libraryMock);
        var service = new DocumentIngestionHostedService(scopeFactory, DefaultSettings, loggerMock.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await service.StartAsync(cts.Token);
            await service.ExecuteTask!;
        }
        catch (OperationCanceledException)
        {
        }

        // Should have recovered from stale lease failure and continued into the main loop
        queueMock.Verify(q => q.TryClaimNextAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private static IServiceScopeFactory CreateScopeFactory(
        Mock<IDocumentIngestionQueue> queueMock,
        Mock<IDocumentLibraryService> libraryMock)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => queueMock.Object);
        services.AddScoped(_ => libraryMock.Object);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
