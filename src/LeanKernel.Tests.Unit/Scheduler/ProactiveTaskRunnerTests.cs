using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Scheduler;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public class ProactiveTaskRunnerTests
{
    [Fact]
    public async Task StartAsync_Disabled_NoScheduling()
    {
        var scheduler = Substitute.For<IScheduler>();
        var job = new LeanKernel.Scheduler.Jobs.WikiMaintenanceJob(
            new LeanKernel.Archivist.Wiki.WikiCompiler(
                Substitute.For<IWikiStore>(),
                Options.Create(new LeanKernelConfig()),
                NullLogger<LeanKernel.Archivist.Wiki.WikiCompiler>.Instance),
            NullLogger<LeanKernel.Scheduler.Jobs.WikiMaintenanceJob>.Instance);

        var config = Options.Create(new LeanKernelConfig { Scheduler = new SchedulerConfig { Enabled = false } });
        var scrubJob = new LeanKernel.Scheduler.Jobs.ChatFactScrubJob(
            Substitute.For<ISessionStore>(),
            Substitute.For<IWikiStore>(),
            NullLogger<LeanKernel.Scheduler.Jobs.ChatFactScrubJob>.Instance);
        var syncJob = new LeanKernel.Scheduler.Jobs.ModelLimitSyncJob(config, NullLogger<LeanKernel.Scheduler.Jobs.ModelLimitSyncJob>.Instance);
        var profileSyncJob = Substitute.For<IAsyncJob>();
        var runner = new ProactiveTaskRunner(scheduler, job, scrubJob, syncJob, profileSyncJob, config, NullLogger<ProactiveTaskRunner>.Instance);

        await runner.StartAsync(CancellationToken.None);

        await scheduler.DidNotReceive().ScheduleAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_Enabled_SchedulesWikiMaintenance()
    {
        var scheduler = Substitute.For<IScheduler>();
        scheduler.ListScheduledJobs().Returns(new List<string> { "wiki-maintenance" });

        var job = new LeanKernel.Scheduler.Jobs.WikiMaintenanceJob(
            new LeanKernel.Archivist.Wiki.WikiCompiler(
                Substitute.For<IWikiStore>(),
                Options.Create(new LeanKernelConfig()),
                NullLogger<LeanKernel.Archivist.Wiki.WikiCompiler>.Instance),
            NullLogger<LeanKernel.Scheduler.Jobs.WikiMaintenanceJob>.Instance);

        var config = Options.Create(new LeanKernelConfig
        {
            Scheduler = new SchedulerConfig
            {
                Enabled = true,
                WikiMaintenanceCron = "0 3 * * *",
                ChatFactScrubCron = "30 2 * * *",
                UserProfileSyncCron = "0 4 * * *"
            }
        });
        var scrubJob = new LeanKernel.Scheduler.Jobs.ChatFactScrubJob(
            Substitute.For<ISessionStore>(),
            Substitute.For<IWikiStore>(),
            NullLogger<LeanKernel.Scheduler.Jobs.ChatFactScrubJob>.Instance);
        var syncJob = new LeanKernel.Scheduler.Jobs.ModelLimitSyncJob(config, NullLogger<LeanKernel.Scheduler.Jobs.ModelLimitSyncJob>.Instance);
        var profileSyncJob = Substitute.For<IAsyncJob>();
        var runner = new ProactiveTaskRunner(scheduler, job, scrubJob, syncJob, profileSyncJob, config, NullLogger<ProactiveTaskRunner>.Instance);

        await runner.StartAsync(CancellationToken.None);

        await scheduler.Received(1).ScheduleAsync(
            "wiki-maintenance", "0 3 * * *",
            Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());

        await scheduler.Received(1).ScheduleAsync(
            "chat-fact-scrub", "30 2 * * *",
            Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());

        await scheduler.Received(1).ScheduleAsync(
            "user-profile-sync", "0 4 * * *",
            Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
    }
}
