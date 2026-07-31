using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

/// <summary>
/// Unit tests for <see cref="EnrichmentQueue"/> covering enqueue, claim,
/// complete, fail (with retry and poison), and stale lease recovery.
/// Uses SQLite in-memory to support the ExecuteSqlRaw claim/lock pattern.
/// </summary>
public class EnrichmentQueueTests : IDisposable
{
    private readonly EntityContext _dbContext;
    private readonly string _dbName;

    public EnrichmentQueueTests()
    {
        _dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite($"DataSource={_dbName}.db")
            .Options;

        _dbContext = new EntityContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        if (File.Exists($"{_dbName}.db"))
        {
            File.Delete($"{_dbName}.db");
        }
    }

    private EnrichmentQueue CreateQueue()
    {
        var factory = new MockDbContextFactory(_dbName);
        return new EnrichmentQueue(factory);
    }

    private static EnrichmentJob CreateTestJob(Guid? ingestionJobId = null)
    {
        return new EnrichmentJob(
            Id: Guid.NewGuid(),
            IngestionJobId: ingestionJobId ?? Guid.NewGuid(),
            FilePath: "staging/doc.pdf",
            FileName: "doc.pdf",
            Fingerprint: "abc123",
            TenantId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            PersonId: Guid.NewGuid(),
            ChannelId: Guid.NewGuid(),
            AvailabilityScope: DocumentAvailabilityScope.User,
            Source: DocumentIngestionSource.Upload);
    }

    private async Task<EnrichmentJobEntity?> GetFirstEntityAsync()
    {
        using var ctx = new EntityContext(new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite($"DataSource={_dbName}.db")
            .Options);
        return await ctx.Set<EnrichmentJobEntity>().FirstOrDefaultAsync();
    }

    [Fact]
    public async Task EnqueueAsync_AddsJobWithPendingStatus()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();

        await queue.EnqueueAsync(job);

        var entity = await GetFirstEntityAsync();
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(Constants.JobStatus.Pending);
        entity.AttemptCount.Should().Be(0);
        entity.FilePath.Should().Be("staging/doc.pdf");
        entity.FileName.Should().Be("doc.pdf");
    }

    [Fact]
    public async Task TryClaimNextAsync_WhenNoJobs_ReturnsNull()
    {
        var queue = CreateQueue();

        var result = await queue.TryClaimNextAsync("worker-1", TimeSpan.FromMinutes(5));

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimNextAsync_ClaimsPendingJob()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        var claimed = await queue.TryClaimNextAsync("worker-1", TimeSpan.FromMinutes(5));

        claimed.Should().NotBeNull();
        claimed!.Status.Should().Be(Constants.JobStatus.Processing);
        claimed.LeaseOwner.Should().Be("worker-1");
        claimed.LeaseExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TryClaimNextAsync_WhenJobHasActiveLease_ReturnsNull()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        var claimed = await queue.TryClaimNextAsync("worker-1", TimeSpan.FromMinutes(5));
        claimed.Should().NotBeNull();

        var secondAttempt = await queue.TryClaimNextAsync("worker-2", TimeSpan.FromMinutes(5));
        secondAttempt.Should().BeNull("because the job is already leased");
    }

    [Fact]
    public async Task TryClaimNextAsync_WhenLeaseExpired_OtherWorkerCanClaim()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        var claimed = await queue.TryClaimNextAsync("worker-1", TimeSpan.FromTicks(1));
        claimed.Should().NotBeNull();

        await Task.Delay(10);

        await queue.RecoverStaleLeasesAsync();

        var reclaimed = await queue.TryClaimNextAsync("worker-2", TimeSpan.FromMinutes(5));
        reclaimed.Should().NotBeNull();
        reclaimed!.LeaseOwner.Should().Be("worker-2");
    }

    [Fact]
    public async Task CompleteAsync_WhenJobExists_UpdatesStatus()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        var claimed = await queue.TryClaimNextAsync("worker-1", TimeSpan.FromMinutes(5));
        claimed.Should().NotBeNull();

        var entityId = (await GetFirstEntityAsync())!.Id;
        var result = new EnrichmentResult(job.IngestionJobId, entityId, null, Success: true);
        await queue.CompleteAsync(entityId, result);

        using var ctx = new EntityContext(new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite($"DataSource={_dbName}.db")
            .Options);
        var entity = await ctx.Set<EnrichmentJobEntity>().FindAsync(entityId);
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(Constants.JobStatus.Completed);
        entity.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_WhenJobMissing_DoesNothing()
    {
        var queue = CreateQueue();
        var result = new EnrichmentResult(Guid.NewGuid(), Guid.NewGuid(), null, Success: true);

        var act = () => queue.CompleteAsync(Guid.NewGuid(), result);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FailAsync_WithRetry_SchedulesRetry()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        var claimed = await queue.TryClaimNextAsync("worker-1", TimeSpan.FromMinutes(5));
        claimed.Should().NotBeNull();

        var entityId = (await GetFirstEntityAsync())!.Id;
        var retryAt = DateTime.UtcNow.AddMinutes(10);
        await queue.FailAsync(entityId, "transient error", retryAt);

        using var ctx = new EntityContext(new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite($"DataSource={_dbName}.db")
            .Options);
        var entity = await ctx.Set<EnrichmentJobEntity>().FindAsync(entityId);
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(Constants.JobStatus.Pending);
        entity.NextAttemptAt.Should().BeCloseTo(retryAt, precision: TimeSpan.FromSeconds(1));
        entity.AttemptCount.Should().Be(1);
        entity.LastError.Should().Be("transient error");
    }

    [Fact]
    public async Task FailAsync_WithoutRetry_PoisonsAfterMaxAttempts()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        await queue.TryClaimNextAsync("worker-1", TimeSpan.FromMinutes(5));

        var entityId = (await GetFirstEntityAsync())!.Id;
        var retryAt = DateTime.UtcNow.AddMinutes(10);
        for (int i = 0; i < 5; i++)
        {
            await queue.FailAsync(entityId, "error", retryAt);
        }

        using var ctx = new EntityContext(new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite($"DataSource={_dbName}.db")
            .Options);
        var entity = await ctx.Set<EnrichmentJobEntity>().FindAsync(entityId);
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(Constants.JobStatus.Poisoned);
        entity.AttemptCount.Should().Be(5);
    }

    [Fact]
    public async Task FailAsync_WithoutRetryAndBelowMax_Retries()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        await queue.TryClaimNextAsync("worker-1", TimeSpan.FromMinutes(5));

        var entityId = (await GetFirstEntityAsync())!.Id;
        await queue.FailAsync(entityId, "error", DateTime.UtcNow.AddMinutes(5));

        using var ctx = new EntityContext(new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite($"DataSource={_dbName}.db")
            .Options);
        var entity = await ctx.Set<EnrichmentJobEntity>().FindAsync(entityId);
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(Constants.JobStatus.Pending);
        entity.NextAttemptAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RecoverStaleLeasesAsync_ResetsExpiredProcessingJobs()
    {
        var queue = CreateQueue();
        var job = CreateTestJob();
        await queue.EnqueueAsync(job);

        var claimed = await queue.TryClaimNextAsync("worker-1", TimeSpan.FromTicks(1));
        claimed.Should().NotBeNull();

        await Task.Delay(10);

        var recovered = await queue.RecoverStaleLeasesAsync();

        recovered.Should().Be(1);

        var entity = await GetFirstEntityAsync();
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(Constants.JobStatus.Pending);
        entity.LeaseOwner.Should().BeNull();
        entity.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task RecoverStaleLeasesAsync_WhenNoStaleJobs_ReturnsZero()
    {
        var queue = CreateQueue();

        var recovered = await queue.RecoverStaleLeasesAsync();

        recovered.Should().Be(0);
    }

    private sealed class MockDbContextFactory : IDbContextFactory<EntityContext>
    {
        private readonly string _dbName;

        public MockDbContextFactory(string dbName)
        {
            _dbName = dbName;
        }

        public EntityContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<EntityContext>()
                .UseSqlite($"DataSource={_dbName}.db")
                .Options;
            var ctx = new EntityContext(options);
            ctx.Database.EnsureCreated();
            return ctx;
        }
    }
}
