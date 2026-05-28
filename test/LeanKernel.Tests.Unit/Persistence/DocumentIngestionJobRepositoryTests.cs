using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence;
using LeanKernel.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeanKernel.Tests.Unit.Persistence;

public class DocumentIngestionJobRepositoryTests
{
    private static LeanKernelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LeanKernelDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;
        return new LeanKernelDbContext(options);
    }

    private static DocumentIngestionJobRepository CreateRepository(LeanKernelDbContext dbContext)
    {
        return new DocumentIngestionJobRepository(dbContext, NullLogger<DocumentIngestionJobRepository>.Instance);
    }

    [Fact]
    public async Task SaveJobAsync_InsertNewJob_SavesSuccessfully()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var job = new DocumentIngestionJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Filename = "test.pdf",
            Title = "Test Document",
            Tags = ["tag1", "tag2"],
            Status = DocumentIngestionStatus.Queued
        };

        // Act
        await repository.SaveJobAsync(job);

        // Assert
        var savedEntity = dbContext.DocumentIngestionJobs.FirstOrDefault(x => x.JobId == job.JobId);
        Assert.NotNull(savedEntity);
        Assert.Equal(job.Filename, savedEntity.Filename);
        Assert.Equal(job.Status.ToString(), savedEntity.Status);
    }

    [Fact]
    public async Task SaveJobAsync_UpdateExistingJob_UpdatesSuccessfully()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var jobId = Guid.NewGuid().ToString("N");
        var job = new DocumentIngestionJob
        {
            JobId = jobId,
            Filename = "test.pdf",
            Title = "Test",
            Tags = ["tag1"],
            Status = DocumentIngestionStatus.Queued
        };

        await repository.SaveJobAsync(job);

        // Change status
        job.Status = DocumentIngestionStatus.Processing;
        job.StartedAt = DateTimeOffset.UtcNow;

        // Act
        await repository.SaveJobAsync(job);

        // Assert
        var updated = dbContext.DocumentIngestionJobs.FirstOrDefault(x => x.JobId == jobId);
        Assert.NotNull(updated);
        Assert.Equal(DocumentIngestionStatus.Processing.ToString(), updated.Status);
        Assert.NotNull(updated.StartedAt);
    }

    [Fact]
    public async Task GetJobAsync_WithValidJobId_ReturnsJob()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var jobId = Guid.NewGuid().ToString("N");
        var job = new DocumentIngestionJob
        {
            JobId = jobId,
            Filename = "test.pdf",
            Title = "Test",
            Tags = ["tag1"],
            Status = DocumentIngestionStatus.Queued
        };

        await repository.SaveJobAsync(job);

        // Act
        var retrieved = await repository.GetJobAsync(jobId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(jobId, retrieved.JobId);
        Assert.Equal("test.pdf", retrieved.Filename);
    }

    [Fact]
    public async Task GetJobAsync_WithInvalidJobId_ReturnsNull()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        // Act
        var retrieved = await repository.GetJobAsync("nonexistent");

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task GetPendingJobsAsync_ReturnsPendingAndProcessingJobs()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var queuedJob = new DocumentIngestionJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Filename = "queued.pdf",
            Status = DocumentIngestionStatus.Queued,
            Tags = []
        };

        var processingJob = new DocumentIngestionJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Filename = "processing.pdf",
            Status = DocumentIngestionStatus.Processing,
            Tags = []
        };

        var completedJob = new DocumentIngestionJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Filename = "completed.pdf",
            Status = DocumentIngestionStatus.Completed,
            Tags = [],
            CompletedAt = DateTimeOffset.UtcNow
        };

        await repository.SaveJobAsync(queuedJob);
        await repository.SaveJobAsync(processingJob);
        await repository.SaveJobAsync(completedJob);

        // Act
        var pending = await repository.GetPendingJobsAsync();

        // Assert
        Assert.NotNull(pending);
        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, j => j.JobId == queuedJob.JobId);
        Assert.Contains(pending, j => j.JobId == processingJob.JobId);
        Assert.DoesNotContain(pending, j => j.JobId == completedJob.JobId);
    }

    [Fact]
    public async Task GetFailedJobsForRetryAsync_ReturnsFailed()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var failedJob1 = new DocumentIngestionJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Filename = "failed1.pdf",
            Status = DocumentIngestionStatus.Failed,
            Tags = [],
            CompletedAt = DateTimeOffset.UtcNow,
            ErrorMessage = "Error 1"
        };

        var failedJob2 = new DocumentIngestionJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Filename = "failed2.pdf",
            Status = DocumentIngestionStatus.Failed,
            Tags = [],
            CompletedAt = DateTimeOffset.UtcNow,
            ErrorMessage = "Error 2"
        };

        var completedJob = new DocumentIngestionJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Filename = "completed.pdf",
            Status = DocumentIngestionStatus.Completed,
            Tags = [],
            CompletedAt = DateTimeOffset.UtcNow
        };

        await repository.SaveJobAsync(failedJob1);
        await repository.SaveJobAsync(failedJob2);
        await repository.SaveJobAsync(completedJob);

        // Act
        var failed = await repository.GetFailedJobsForRetryAsync(maxRetries: 3);

        // Assert
        Assert.NotNull(failed);
        Assert.Equal(2, failed.Count);
        Assert.Contains(failed, j => j.JobId == failedJob1.JobId);
        Assert.Contains(failed, j => j.JobId == failedJob2.JobId);
    }

    [Fact]
    public async Task IncrementRetryCountAsync_IncrementsRetries()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var jobId = Guid.NewGuid().ToString("N");
        var job = new DocumentIngestionJob
        {
            JobId = jobId,
            Filename = "test.pdf",
            Status = DocumentIngestionStatus.Failed,
            Tags = []
        };

        await repository.SaveJobAsync(job);

        // Act
        await repository.IncrementRetryCountAsync(jobId);

        // Assert
        var updated = dbContext.DocumentIngestionJobs.FirstOrDefault(x => x.JobId == jobId);
        Assert.NotNull(updated);
        Assert.Equal(1, updated.RetryCount);
    }

    [Fact]
    public async Task DeleteJobAsync_RemovesJob()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var jobId = Guid.NewGuid().ToString("N");
        var job = new DocumentIngestionJob
        {
            JobId = jobId,
            Filename = "test.pdf",
            Status = DocumentIngestionStatus.Completed,
            Tags = [],
            CompletedAt = DateTimeOffset.UtcNow
        };

        await repository.SaveJobAsync(job);

        // Act
        await repository.DeleteJobAsync(jobId);

        // Assert
        var deleted = dbContext.DocumentIngestionJobs.FirstOrDefault(x => x.JobId == jobId);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task SaveJobAsync_WithResult_SerializesResultToJson()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var jobId = Guid.NewGuid().ToString("N");
        var result = new DocumentIngestionResult
        {
            PageSlug = "test-doc",
            Title = "Test Document",
            ExtractedLength = 1000,
            RelativeFilePath = "test.pdf",
            FileStoragePath = "/storage/test.pdf"
        };

        var job = new DocumentIngestionJob
        {
            JobId = jobId,
            Filename = "test.pdf",
            Status = DocumentIngestionStatus.Completed,
            Tags = [],
            Result = result,
            CompletedAt = DateTimeOffset.UtcNow
        };

        // Act
        await repository.SaveJobAsync(job);

        // Assert
        var saved = dbContext.DocumentIngestionJobs.FirstOrDefault(x => x.JobId == jobId);
        Assert.NotNull(saved);
        Assert.NotNull(saved.Result);
        Assert.Contains("test-doc", saved.Result);
    }

    [Fact]
    public async Task SaveJobAsync_SavesAndRetrievesWithTags()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var jobId = Guid.NewGuid().ToString("N");
        var tags = new List<string> { "important", "urgent", "review" };
        var job = new DocumentIngestionJob
        {
            JobId = jobId,
            Filename = "test.pdf",
            Title = "Multi-tag test",
            Tags = tags,
            Status = DocumentIngestionStatus.Queued
        };

        // Act
        await repository.SaveJobAsync(job);
        var retrieved = await repository.GetJobAsync(jobId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(3, retrieved.Tags.Count);
        Assert.Contains("important", retrieved.Tags);
        Assert.Contains("urgent", retrieved.Tags);
        Assert.Contains("review", retrieved.Tags);
    }

    [Fact]
    public async Task SaveJobAsync_HandlesEmptyTags()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var jobId = Guid.NewGuid().ToString("N");
        var job = new DocumentIngestionJob
        {
            JobId = jobId,
            Filename = "test.pdf",
            Tags = new List<string>(),
            Status = DocumentIngestionStatus.Queued
        };

        // Act
        await repository.SaveJobAsync(job);
        var retrieved = await repository.GetJobAsync(jobId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Empty(retrieved.Tags);
    }

    [Fact]
    public async Task SaveJobAsync_HandlesNullTitle()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var jobId = Guid.NewGuid().ToString("N");
        var job = new DocumentIngestionJob
        {
            JobId = jobId,
            Filename = "test.pdf",
            Title = null,
            Tags = [],
            Status = DocumentIngestionStatus.Queued
        };

        // Act
        await repository.SaveJobAsync(job);
        var retrieved = await repository.GetJobAsync(jobId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Null(retrieved.Title);
    }

    [Fact]
    public async Task GetPendingJobsAsync_OrdersByCreatedAt()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var job1 = new DocumentIngestionJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Filename = "job1.pdf",
            Tags = [],
            Status = DocumentIngestionStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-10)
        };

        var job2 = new DocumentIngestionJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Filename = "job2.pdf",
            Tags = [],
            Status = DocumentIngestionStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.SaveJobAsync(job1);
        await repository.SaveJobAsync(job2);

        // Act
        var pending = await repository.GetPendingJobsAsync();

        // Assert - should be ordered by creation date
        Assert.NotNull(pending);
        Assert.Equal(2, pending.Count);
        Assert.Equal(job1.JobId, pending[0].JobId);
        Assert.Equal(job2.JobId, pending[1].JobId);
    }

    [Fact]
    public async Task GetFailedJobsForRetryAsync_RespectsRetryLimit()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var job1 = new DocumentIngestionJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Filename = "job1.pdf",
            Tags = [],
            Status = DocumentIngestionStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            ErrorMessage = "Error"
        };

        var job2 = new DocumentIngestionJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Filename = "job2.pdf",
            Tags = [],
            Status = DocumentIngestionStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            ErrorMessage = "Error"
        };

        await repository.SaveJobAsync(job1);
        await repository.SaveJobAsync(job2);

        // Mark job2 as retried 3 times
        var entity2 = dbContext.DocumentIngestionJobs.FirstOrDefault(x => x.JobId == job2.JobId);
        if (entity2 != null)
        {
            entity2.RetryCount = 3;
            await dbContext.SaveChangesAsync();
        }

        // Act - retrieve failed jobs with max retries of 3
        var failed = await repository.GetFailedJobsForRetryAsync(maxRetries: 3);

        // Assert - only job1 should be returned (job2 has reached max retries)
        Assert.NotNull(failed);
        Assert.Single(failed);
        Assert.Equal(job1.JobId, failed[0].JobId);
    }

    [Fact]
    public async Task SaveJobAsync_PreservesRetryCountOnRoundTrip()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var jobId = Guid.NewGuid().ToString("N");
        var job = new DocumentIngestionJob
        {
            JobId = jobId,
            Filename = "test.pdf",
            Tags = [],
            Status = DocumentIngestionStatus.Failed,
            RetryCount = 2
        };

        // Act
        await repository.SaveJobAsync(job);
        var retrieved = await repository.GetJobAsync(jobId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.RetryCount);
    }

    [Fact]
    public async Task SaveJobAsync_HandlesTagsWithCommas()
    {
        // Arrange
        var dbContext = CreateDbContext();
        var repository = CreateRepository(dbContext);

        var jobId = Guid.NewGuid().ToString("N");
        var job = new DocumentIngestionJob
        {
            JobId = jobId,
            Filename = "test.pdf",
            Tags = ["tag, with comma", "normal-tag"],
            Status = DocumentIngestionStatus.Queued
        };

        // Act
        await repository.SaveJobAsync(job);
        var retrieved = await repository.GetJobAsync(jobId);

        // Assert - tags with commas should survive round-trip
        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.Tags.Count);
        Assert.Contains("tag, with comma", retrieved.Tags);
        Assert.Contains("normal-tag", retrieved.Tags);
    }
}
