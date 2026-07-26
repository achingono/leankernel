using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Services.Common.Interfaces;
using LeanKernel.Services.Learning.Scheduler;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class JobExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_UnknownJobType_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var executor = new JobExecutor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<JobExecutor>.Instance);

        var job = new ScheduledJobEntity
        {
            Id = Guid.NewGuid(),
            Name = "unknown-job",
            JobType = "Unknown",
            CronExpression = "*/5 * * * * *",
            ConfigurationJson = "{}",
            TenantId = Guid.NewGuid(),
            NextRunAt = DateTime.UtcNow,
        };

        var act = async () => await executor.ExecuteAsync(job);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_DreamCycle_InvokesDreamService()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new EntityContext(options))
        {
            await context.Database.EnsureCreatedAsync();
        }

        var dreamService = new Mock<IDreamService>();
        dreamService
            .Setup(x => x.RunDreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DreamRunResult("default", "full", "Completed", 0, 0, null));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDreamService>(dreamService.Object);
        services.AddSingleton<IDbContextFactory<EntityContext>>(new TestContextFactory(options));

        var provider = services.BuildServiceProvider();
        var executor = new JobExecutor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<JobExecutor>.Instance);

        var job = new ScheduledJobEntity
        {
            Id = Guid.NewGuid(),
            Name = "dream-job",
            JobType = "DreamCycle",
            CronExpression = "*/5 * * * * *",
            ConfigurationJson = "{}",
            TenantId = Guid.NewGuid(),
            NextRunAt = DateTime.UtcNow,
        };

        await executor.ExecuteAsync(job);

        dreamService.Verify(x => x.RunDreamAsync("default", "full", It.IsAny<CancellationToken>()), Times.Once);
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