using System.Data.Common;

using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using LeanKernel.Tests.Unit.TestDoubles;

using Xunit;

namespace LeanKernel.Tests.Unit.DocumentIngestion;

public sealed class DocumentIngestionQueueTests : IDisposable
{
    private readonly DbConnection _connection;
    private readonly DbContextOptions<EntityContext> _contextOptions;

    public DocumentIngestionQueueTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _contextOptions = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new EntityContext(_contextOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private IDocumentIngestionQueue CreateQueue()
    {
        var factory = new TestDbContextFactory(_contextOptions);
        return new DocumentIngestionQueue(factory);
    }

    [Fact]
    public async Task EnqueueAsync_AddsPendingJob()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();

        await queue.EnqueueAsync(job);

        using var context = new EntityContext(_contextOptions);
        var entity = await context.DocumentIngestionJobs.FirstOrDefaultAsync();
        entity.Should().NotBeNull();
        entity!.Status.Should().Be("Pending");
        entity.FileName.Should().Be(job.FileName);
        entity.TenantId.Should().Be(job.TenantId);
    }

    [Fact]
    public async Task TryClaimNextAsync_ReturnsNull_WhenNoPendingJobs()
    {
        var queue = CreateQueue();
        var result = await queue.TryClaimNextAsync("worker1", TimeSpan.FromMinutes(5));
        result.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimNextAsync_ClaimsEligibleJob()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        var claimed = await queue.TryClaimNextAsync("worker1", TimeSpan.FromMinutes(5));

        claimed.Should().NotBeNull();
        claimed!.Status.Should().Be("Processing");
        claimed.LeaseOwner.Should().Be("worker1");
        claimed.LeaseExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task TryClaimNextAsync_DoesNotClaim_AlreadyProcessingJob()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        var first = await queue.TryClaimNextAsync("worker1", TimeSpan.FromMinutes(5));
        first.Should().NotBeNull();

        var second = await queue.TryClaimNextAsync("worker2", TimeSpan.FromMinutes(5));
        second.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimNextAsync_DoesNotClaimExpiredProcessingLease()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        using (var context = new EntityContext(_contextOptions))
        {
            var entity = await context.DocumentIngestionJobs.FirstAsync();
            entity.Status = "Processing";
            entity.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            entity.LeaseOwner = "dead-worker";
            await context.SaveChangesAsync();
        }

        var claimed = await queue.TryClaimNextAsync("worker2", TimeSpan.FromMinutes(5));
        claimed.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_MarksCompleted()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);
        var claimed = await queue.TryClaimNextAsync("worker1", TimeSpan.FromMinutes(5));

        var result = new IngestionResult("abc123", true, false);
        await queue.CompleteAsync(claimed!.Id, result);

        using var context = new EntityContext(_contextOptions);
        var entity = await context.DocumentIngestionJobs.FindAsync(claimed.Id);
        entity.Should().NotBeNull();
        entity!.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task FailAsync_SchedulesRetry_WhenUnderBudget()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);
        var claimed = await queue.TryClaimNextAsync("worker1", TimeSpan.FromMinutes(5));

        var retryAt = DateTime.UtcNow.AddMinutes(1);
        await queue.FailAsync(claimed!.Id, "error", retryAt);

        using var context = new EntityContext(_contextOptions);
        var entity = await context.DocumentIngestionJobs.FindAsync(claimed.Id);
        entity.Should().NotBeNull();
        entity!.Status.Should().Be("Pending");
        entity.AttemptCount.Should().Be(1);
        entity.NextAttemptAt.Should().BeCloseTo(retryAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FailAsync_PoisonsJob_WhenOverBudget()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        Guid jobId;
        using (var context = new EntityContext(_contextOptions))
        {
            var entity = await context.DocumentIngestionJobs.FirstAsync();
            entity.AttemptCount = 5;
            await context.SaveChangesAsync();
            jobId = entity.Id;
        }

        await queue.FailAsync(jobId, "exhausted", DateTime.UtcNow.AddMinutes(1));

        using var context2 = new EntityContext(_contextOptions);
        var reloaded = await context2.DocumentIngestionJobs.FindAsync(jobId);
        reloaded!.Status.Should().Be("Poisoned");
    }

    [Fact]
    public async Task RecoverStaleLeasesAsync_ResetsExpiredProcessingJobs()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        using (var context = new EntityContext(_contextOptions))
        {
            var entity = await context.DocumentIngestionJobs.FirstAsync();
            entity.Status = "Processing";
            entity.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            entity.LeaseOwner = "dead-worker";
            await context.SaveChangesAsync();
        }

        var recovered = await queue.RecoverStaleLeasesAsync();
        recovered.Should().Be(1);

        using var context2 = new EntityContext(_contextOptions);
        var reloaded = await context2.DocumentIngestionJobs.FirstAsync();
        reloaded.Status.Should().Be("Pending");
        reloaded.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task RecoverStaleLeasesAsync_DoesNotTouchActiveLeases()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        using (var context = new EntityContext(_contextOptions))
        {
            var entity = await context.DocumentIngestionJobs.FirstAsync();
            entity.Status = "Processing";
            entity.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5);
            await context.SaveChangesAsync();
        }

        var recovered = await queue.RecoverStaleLeasesAsync();
        recovered.Should().Be(0);
    }

    [Fact]
    public async Task CompleteAsync_WithDuplicate_RecordsLastError()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);
        var claimed = await queue.TryClaimNextAsync("worker1", TimeSpan.FromMinutes(5));

        var result = new IngestionResult("abc123", true, true);
        await queue.CompleteAsync(claimed!.Id, result);

        using var context = new EntityContext(_contextOptions);
        var entity = await context.DocumentIngestionJobs.FindAsync(claimed.Id);
        entity!.LastError.Should().Be("Duplicate");
    }

    private static DocumentIngestionJob CreateTestJob() => new(
        "/tmp/test.txt",
        "test.txt",
        "text/plain",
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        DocumentAvailabilityScope.User,
        DocumentIngestionSource.Upload);
}
