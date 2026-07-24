using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Services.Common.Interfaces;
using LeanKernel.Services.Learning.Scheduler;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class DreamCycleJobHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_ValidRun_PersistsCompletedRecord()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var dreamService = new Mock<IDreamService>();
        dreamService
            .Setup(x => x.RunDreamAsync("scope-a", "targeted", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DreamRunResult("scope-a", "targeted", "Completed", 5, 1, "{\"phase\":\"ok\"}"));

        var handler = fixture.CreateHandler(dreamService.Object);

        await handler.ExecuteAsync(Guid.NewGuid(), "{\"SourceScope\":\"scope-a\",\"Mode\":\"targeted\",\"LockTimeoutSeconds\":15}");

        await using var context = await fixture.ContextFactory.CreateDbContextAsync();
        var records = await context.DreamRunRecords.ToListAsync();

        records.Should().ContainSingle();
        records[0].SourceScope.Should().Be("scope-a");
        records[0].Mode.Should().Be("targeted");
        records[0].Status.Should().Be("Completed");
        records[0].TotalPages.Should().Be(5);
        records[0].FailedPages.Should().Be(1);
        records[0].CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_DreamTimeout_MarksTimedOutAndReschedules()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var jobId = Guid.NewGuid();
        await using (var seedContext = await fixture.ContextFactory.CreateDbContextAsync())
        {
            seedContext.ScheduledJobs.Add(new ScheduledJobEntity
            {
                Id = jobId,
                Name = "dream-job",
                JobType = "DreamCycle",
                CronExpression = "*/15 * * * * *",
                ConfigurationJson = "{}",
                TenantId = Guid.NewGuid(),
                NextRunAt = DateTime.UtcNow.AddMinutes(-5),
            });

            await seedContext.SaveChangesAsync();
        }

        var dreamService = new Mock<IDreamService>();
        dreamService
            .Setup(x => x.RunDreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>(async (_, _, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                return new DreamRunResult("default", "full", "Completed", 0, 0, null);
            });

        var handler = fixture.CreateHandler(dreamService.Object);

        await handler.ExecuteAsync(jobId, "{\"LockTimeoutSeconds\":1}");

        await using var assertContext = await fixture.ContextFactory.CreateDbContextAsync();
        var record = await assertContext.DreamRunRecords.SingleAsync();
        var job = await assertContext.ScheduledJobs.SingleAsync();

        record.Status.Should().Be("TimedOut");
        record.CompletedAt.Should().NotBeNull();
        job.NextRunAt.Should().BeAfter(DateTime.UtcNow.AddSeconds(-10));
    }

    [Fact]
    public async Task ExecuteAsync_LockContention_PersistsSkippedRun()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var jobId = Guid.NewGuid();
        await using (var seedContext = await fixture.ContextFactory.CreateDbContextAsync())
        {
            seedContext.ScheduledJobs.Add(new ScheduledJobEntity
            {
                Id = jobId,
                Name = "dream-job-contention",
                JobType = "DreamCycle",
                CronExpression = "*/20 * * * * *",
                ConfigurationJson = "{}",
                TenantId = Guid.NewGuid(),
                NextRunAt = DateTime.UtcNow.AddMinutes(-5),
            });

            await seedContext.SaveChangesAsync();
        }

        var dreamService = new Mock<IDreamService>();
        dreamService
            .Setup(x => x.RunDreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>(async (_, _, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                return new DreamRunResult("contended", "full", "Completed", 1, 0, null);
            });

        var handler = fixture.CreateHandler(dreamService.Object);

        var first = handler.ExecuteAsync(Guid.NewGuid(), "{\"SourceScope\":\"contended\",\"LockTimeoutSeconds\":5}");
        await Task.Delay(200);
        var second = handler.ExecuteAsync(jobId, "{\"SourceScope\":\"contended\",\"LockTimeoutSeconds\":1}");

        await Task.WhenAll(first, second);

        await using var assertContext = await fixture.ContextFactory.CreateDbContextAsync();
        var records = await assertContext.DreamRunRecords.ToListAsync();

        records.Should().Contain(x => x.Status == "Completed");
        records.Should().Contain(x => x.Status == "SkippedDueToLock");
        dreamService.Verify(x => x.RunDreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestFixture(SqliteConnection connection, TestContextFactory contextFactory)
        {
            _connection = connection;
            ContextFactory = contextFactory;
        }

        public TestContextFactory ContextFactory { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<EntityContext>()
                .UseSqlite(connection)
                .Options;

            var contextFactory = new TestContextFactory(options);

            await using var context = await contextFactory.CreateDbContextAsync();
            await context.Database.EnsureCreatedAsync();

            return new TestFixture(connection, contextFactory);
        }

        public DreamCycleJobHandler CreateHandler(IDreamService dreamService)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IDreamService>(dreamService);
            services.AddSingleton<IDbContextFactory<EntityContext>>(ContextFactory);

            var provider = services.BuildServiceProvider();
            return new DreamCycleJobHandler(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DreamCycleJobHandler>.Instance);
        }

        public ValueTask DisposeAsync()
        {
            return _connection.DisposeAsync();
        }
    }

    private sealed class TestContextFactory(DbContextOptions<EntityContext> options) : IDbContextFactory<EntityContext>
    {
        public EntityContext CreateDbContext()
        {
            return new EntityContext(options);
        }

        public Task<EntityContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EntityContext(options));
        }
    }
}
