using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Services.Learning.Scheduler;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class DbScheduledJobDefinitionProviderTests
{
    [Fact]
    public async Task GetEnabledJobsAsync_ReturnsEnabledJobsWithTenantAndChannelScopes()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(connection)
            .Options;

        var tenantId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var seedContext = new EntityContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync();

            seedContext.Tenants.Add(new TenantEntity
            {
                Id = tenantId,
                Name = "Tenant A",
                Description = "test",
                HostName = "tenant-a.local",
                IsActive = true,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge { Id = userId, FullName = "Test User", Email = "test@example.com" }
            });

            seedContext.Channels.Add(new ChannelEntity { Id = channelId, Name = "openai-http" });

            seedContext.ScheduledJobs.AddRange(
                new ScheduledJobEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = "tenant-job",
                    Cron = "*/5 * * * *",
                    Enabled = true,
                    JobType = "learning.ping"
                },
                new ScheduledJobEntity
                {
                    Id = Guid.NewGuid(),
                    ChannelId = channelId,
                    Name = "channel-job",
                    Cron = "*/5 * * * *",
                    Enabled = true,
                    JobType = "learning.ping"
                },
                new ScheduledJobEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ChannelId = channelId,
                    Name = "tenant-channel-job",
                    Cron = "*/5 * * * *",
                    Enabled = true,
                    JobType = "learning.ping"
                },
                new ScheduledJobEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = "disabled-job",
                    Cron = "*/5 * * * *",
                    Enabled = false,
                    JobType = "learning.ping"
                });

            await seedContext.SaveChangesAsync();
        }

        await using var queryContext = new EntityContext(options);
        var provider = new DbScheduledJobDefinitionProvider(new TestDbContextFactory(queryContext));

        var jobs = await provider.GetEnabledJobsAsync(CancellationToken.None);

        jobs.Should().HaveCount(3);
        jobs.Should().ContainSingle(job => job.Name == "tenant-job" && job.TenantId == tenantId && job.ChannelId is null);
        jobs.Should().ContainSingle(job => job.Name == "channel-job" && job.TenantId is null && job.ChannelId == channelId);
        jobs.Should().ContainSingle(job => job.Name == "tenant-channel-job" && job.TenantId == tenantId && job.ChannelId == channelId);
    }

    private sealed class TestDbContextFactory(EntityContext context) : IDbContextFactory<EntityContext>
    {
        public Task<EntityContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(context);

        public EntityContext CreateDbContext() => context;
    }
}
        jobs.Should().ContainSingle(job => job.Name == "tenant-job" && job.TenantId == tenantId && job.ChannelId == null);
        jobs.Should().ContainSingle(job => job.Name == "channel-job" && job.TenantId == null && job.ChannelId == channelId);
