using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Scheduler;

namespace LeanKernel.Tests.Unit.Scheduler;

public class CronSchedulerTests
{
    [Fact]
    public async Task ScheduleAsync_AddsJob()
    {
        var scheduler = new CronScheduler(NullLogger<CronScheduler>.Instance);

        await scheduler.ScheduleAsync("test-job", "*/5 * * * *", async _ =>
        {
            await Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Contains("test-job", scheduler.ListScheduledJobs());
    }

    [Fact]
    public async Task ScheduleAsync_ThrowsOnDuplicate()
    {
        var scheduler = new CronScheduler(NullLogger<CronScheduler>.Instance);

        await scheduler.ScheduleAsync("job-1", "0 * * * *", _ => Task.CompletedTask, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scheduler.ScheduleAsync("job-1", "0 * * * *", _ => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task CancelAsync_RemovesJob()
    {
        var scheduler = new CronScheduler(NullLogger<CronScheduler>.Instance);

        await scheduler.ScheduleAsync("cancel-me", "0 * * * *", _ => Task.CompletedTask, CancellationToken.None);
        Assert.Contains("cancel-me", scheduler.ListScheduledJobs());

        await scheduler.CancelAsync("cancel-me", CancellationToken.None);
        Assert.DoesNotContain("cancel-me", scheduler.ListScheduledJobs());
    }

    [Fact]
    public async Task ListScheduledJobs_ReturnsAllJobs()
    {
        var scheduler = new CronScheduler(NullLogger<CronScheduler>.Instance);

        await scheduler.ScheduleAsync("a", "0 * * * *", _ => Task.CompletedTask, CancellationToken.None);
        await scheduler.ScheduleAsync("b", "0 * * * *", _ => Task.CompletedTask, CancellationToken.None);

        var jobs = scheduler.ListScheduledJobs();
        Assert.Equal(2, jobs.Count);
    }
}
