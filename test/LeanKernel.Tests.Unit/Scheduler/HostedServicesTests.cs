using FluentAssertions;

using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Common.Queue;
using LeanKernel.Services.Common.Scheduler;
using LeanKernel.Services.Learning.Configuration;
using LeanKernel.Services.Learning.Learning;
using LeanKernel.Services.Learning.Scheduler;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class HostedServicesTests
{
    [Fact]
    public async Task LearningBackgroundWorker_WhenEnabled_ExecutesPipelineForQueuedTurn()
    {
        var queue = new BoundedTurnEventQueue(8);
        await queue.EnqueueAsync(CreateTurnEvent("turn-worker"), CancellationToken.None);

        var pipeline = new Mock<ISelfImprovementPipeline>();
        var worker = new LearningBackgroundWorker(
            CreateScopeFactory(pipeline.Object),
            queue,
            Options.Create(new LearningRuntimeOptions { Enabled = true }),
            Mock.Of<ILogger<LearningBackgroundWorker>>());

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        pipeline.Verify(candidate => candidate.ExecuteAsync(It.IsAny<CompletedTurnEvent>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private static IServiceScopeFactory CreateScopeFactory(params object[] services)
    [Fact]
    public async Task LearningBackgroundWorker_WhenDisabled_DoesNotExecutePipeline()
    {
        var queue = new BoundedTurnEventQueue(8);
        await queue.EnqueueAsync(CreateTurnEvent("turn-worker-disabled"), CancellationToken.None);

        var pipeline = new Mock<ISelfImprovementPipeline>();
        var worker = new LearningBackgroundWorker(
            CreateScopeFactory(pipeline.Object),
            queue,
            Options.Create(new LearningRuntimeOptions { Enabled = false }),
            Mock.Of<ILogger<LearningBackgroundWorker>>());

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        pipeline.Verify(candidate => candidate.ExecuteAsync(It.IsAny<CompletedTurnEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SchedulerHostedService_ExecutesDueJob()
    {
        var executor = new Mock<IScheduledJobExecutor>();
        var jobs = new Mock<IScheduledJobDefinitionProvider>();
        jobs.Setup(candidate => candidate.GetEnabledJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ScheduledJobDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "due-job",
                    Cron = "*/5 * * * *",
                    Enabled = true,
                    JobType = ScheduledJobTypes.LearningPing
                }
            ]);

        var scheduler = new SchedulerHostedService(
            CreateScopeFactory(executor.Object, jobs.Object),
            CreateScopeFactory(executor.Object, jobs.Object),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 19, 20, 10, 0, TimeSpan.Zero)),
            Options.Create(new SchedulerRuntimeOptions
            {
                Enabled = true,
                PollIntervalSeconds = 1
                        Name = "due-job",
                        Cron = "*/5 * * * *",
                        Enabled = true,
                        JobType = ScheduledJobTypes.LearningPing
                    }
                ]
            }),
            Mock.Of<ILogger<SchedulerHostedService>>());

        await scheduler.StartAsync(CancellationToken.None);
        await Task.Delay(1200);
        await scheduler.StopAsync(CancellationToken.None);

        executor.Verify(candidate => candidate.ExecuteAsync(
            It.Is<ScheduledJobDefinition>(job => job.Name == "due-job"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SchedulerHostedService_WhenDisabled_DoesNotExecuteJobs()
    {
        var executor = new Mock<IScheduledJobExecutor>();
        var jobs = new Mock<IScheduledJobDefinitionProvider>();
        var scheduler = new SchedulerHostedService(
            CreateScopeFactory(executor.Object),
            new FixedTimeProvider(DateTimeOffset.UtcNow),
            Options.Create(new SchedulerRuntimeOptions
            {
                Enabled = false
                        Name = "disabled-job",
                        Cron = "* * * * *",
                        Enabled = true,
                        JobType = ScheduledJobTypes.LearningPing
                    }
                ]
            }),
            Mock.Of<ILogger<SchedulerHostedService>>());

        await scheduler.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await scheduler.StopAsync(CancellationToken.None);

        executor.Verify(candidate => candidate.ExecuteAsync(It.IsAny<ScheduledJobDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
        jobs.Verify(candidate => candidate.GetEnabledJobsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IServiceScopeFactory CreateScopeFactory(object service)
    {
        var provider = new Mock<IServiceProvider>();
        provider
            .Setup(candidate => candidate.GetService(It.IsAny<Type>()))
            .Returns<Type>(type => services.FirstOrDefault(type.IsInstanceOfType));

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(provider.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(candidate => candidate.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private static CompletedTurnEvent CreateTurnEvent(string turnId)
    {
        return new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-hosted",
            turnId,
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", "hello")],
            [new TurnMessage("assistant", "world")]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
