using System.Reflection;

using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Services.Learning.Scheduler;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class SchedulerHostedServiceTests
{
    [Fact]
    public async Task EvaluateAndExecuteJobsAsync_DueJob_UpdatesScheduleMetadata()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<EntityContext>(o => o.UseSqlite(connection));
        services.AddDbContextFactory<EntityContext>(o => o.UseSqlite(connection));
        services.AddScoped<CronScheduleEvaluator>();
        services.AddScoped(sp => new JobExecutor(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<JobExecutor>.Instance));
        var provider = services.BuildServiceProvider();

        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<EntityContext>();
            await db.Database.EnsureCreatedAsync();
            db.ScheduledJobs.Add(new ScheduledJobEntity
            {
                Id = Guid.NewGuid(),
                Name = "scheduler-test-job",
                JobType = "Unknown",
                CronExpression = "not-a-cron",
                ConfigurationJson = "{}",
                TenantId = Guid.NewGuid(),
                NextRunAt = DateTime.UtcNow.AddMinutes(-1),
                Enabled = true,
            });

            await db.SaveChangesAsync();
        }

        var sut = new SchedulerHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SchedulerSettings { PollIntervalSeconds = 1 }),
            healthState: null,
            logger: NullLogger<SchedulerHostedService>.Instance);

        await InvokePrivateAsync(sut, "EvaluateAndExecuteJobsAsync", CancellationToken.None);

        await using var assertScope = provider.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<EntityContext>();
        var job = await assertDb.ScheduledJobs.SingleAsync();

        job.LastRunAt.Should().NotBeNull();
        job.UpdatedAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-5));
        job.NextRunAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledToken_StopsCleanly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<EntityContext>(o => o.UseSqlite(connection));
        services.AddDbContextFactory<EntityContext>(o => o.UseSqlite(connection));
        services.AddScoped<CronScheduleEvaluator>();
        services.AddScoped(sp => new JobExecutor(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<JobExecutor>.Instance));
        var provider = services.BuildServiceProvider();

        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<EntityContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var sut = new SchedulerHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SchedulerSettings { PollIntervalSeconds = 1 }),
            healthState: null,
            logger: NullLogger<SchedulerHostedService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await InvokePrivateAsync(sut, "ExecuteAsync", cts.Token);
    }

    private static async Task InvokePrivateAsync(object target, string methodName, CancellationToken ct)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = (Task?)method!.Invoke(target, [ct]);
        task.Should().NotBeNull();
        await task!;
    }
}