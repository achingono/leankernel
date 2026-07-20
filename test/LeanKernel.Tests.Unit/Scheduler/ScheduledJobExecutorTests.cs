using FluentAssertions;

    private static ScheduledJobExecutor CreateExecutor(
        Mock<ISelfImprovementPipeline>? pipeline = null,
        Mock<ILearningStepRunner>? stepRunner = null,
        Mock<IOnboardingGapDetector>? onboardingGapDetector = null,
        Mock<IOnboardingDirectiveBuilder>? onboardingDirectiveBuilder = null,
        Mock<IOnboardingDirectivePublisher>? onboardingDirectivePublisher = null)
using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Common.Scheduler;
using LeanKernel.Services.Learning.Learning;
using LeanKernel.Services.Learning.Scheduler;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class ScheduledJobExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_UnknownJobType_ThrowsInvalidOperationException()
    {
        var executor = CreateExecutor();
        var job = new ScheduledJobDefinition
        {
            Name = "unknown-job",
            JobType = "learning.unknown"
        };

        var act = () => executor.ExecuteAsync(job, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*learning.unknown*");
    }

    [Fact]
    public async Task ExecuteAsync_ReplayTurn_InvokesPipeline()
    {
        var pipeline = new Mock<ISelfImprovementPipeline>();
        var executor = CreateExecutor(pipeline: pipeline);

        var turn = new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-1",
            "turn-1",
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", "hello")],
            [new TurnMessage("assistant", "world")]);

        var payload = System.Text.Json.JsonSerializer.Serialize(turn);
        var job = new ScheduledJobDefinition
        {
            Name = "replay-job",
            JobType = ScheduledJobTypes.LearningReplayTurn,
            Payload = payload
        };

        await executor.ExecuteAsync(job, CancellationToken.None);

        pipeline.Verify(
            candidate => candidate.ExecuteAsync(
                It.Is<CompletedTurnEvent>(evt => evt.TurnId == "turn-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJsonPayload_ThrowsInvalidOperationException()
    {
        var executor = CreateExecutor();
        var job = new ScheduledJobDefinition
        {
            Name = "broken-payload",
            JobType = ScheduledJobTypes.LearningPing,
            Payload = "{invalid-json"
        };

        var act = () => executor.ExecuteAsync(job, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid JSON payload*");
    }

    [Fact]
    public async Task ExecuteAsync_ExecuteLearningStep_InvokesStepRunner()
    {
        var stepRunner = new Mock<ILearningStepRunner>();
        var executor = CreateExecutor(stepRunner: stepRunner);

        var turn = new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-2",
            "turn-2",
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", "hello")],
            [new TurnMessage("assistant", "world")]);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            stepName = "identity-intent",
            turnEvent = turn
        });

        var job = new ScheduledJobDefinition
        {
            Name = "run-step",
            JobType = ScheduledJobTypes.LearningExecuteStep,
            Payload = payload
        };

        await executor.ExecuteAsync(job, CancellationToken.None);

        stepRunner.Verify(
            runner => runner.ExecuteStepAsync(
                "identity-intent",
                It.Is<CompletedTurnEvent>(evt => evt.TurnId == "turn-2"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OnboardingDetectGaps_PublishesDirective()
    {
        var detector = new Mock<IOnboardingGapDetector>();
        var builder = new Mock<IOnboardingDirectiveBuilder>();
        var publisher = new Mock<IOnboardingDirectivePublisher>();
        detector.Setup(candidate => candidate.DetectGaps(It.IsAny<CompletedTurnEvent>())).Returns(["email"]);
        builder.Setup(candidate => candidate.BuildDirective(It.IsAny<CompletedTurnEvent>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("Ask for email.");

        var executor = CreateExecutor(onboardingGapDetector: detector, onboardingDirectiveBuilder: builder, onboardingDirectivePublisher: publisher);

        var turn = new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-3",
            "turn-3",
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", "hi")],
            [new TurnMessage("assistant", "hello")]);

        var payload = System.Text.Json.JsonSerializer.Serialize(turn);
        var job = new ScheduledJobDefinition
        {
            Name = "onboarding-gaps",
            JobType = ScheduledJobTypes.OnboardingDetectGaps,
            Payload = payload
        };

        await executor.ExecuteAsync(job, CancellationToken.None);

        publisher.Verify(
            candidate => candidate.PublishAsync(
                It.Is<CompletedTurnEvent>(evt => evt.TurnId == "turn-3"),
                "Ask for email.",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ScheduledJobExecutor CreateExecutor(Mock<ISelfImprovementPipeline>? pipeline = null)
    {
        pipeline ??= new Mock<ISelfImprovementPipeline>();
        stepRunner ??= new Mock<ILearningStepRunner>();
        onboardingGapDetector ??= new Mock<IOnboardingGapDetector>();
        onboardingDirectiveBuilder ??= new Mock<IOnboardingDirectiveBuilder>();
        onboardingDirectivePublisher ??= new Mock<IOnboardingDirectivePublisher>();

        var handlers = new IScheduledJobHandler[]
        {
            new PingScheduledJobHandler(Mock.Of<ILogger<PingScheduledJobHandler>>()),
            new ReplayTurnScheduledJobHandler(pipeline.Object, Mock.Of<ILogger<ReplayTurnScheduledJobHandler>>()),
            new ExecuteLearningStepScheduledJobHandler(stepRunner.Object, Mock.Of<ILogger<ExecuteLearningStepScheduledJobHandler>>()),
            new OnboardingGapDetectionScheduledJobHandler(
                onboardingGapDetector.Object,
                onboardingDirectiveBuilder.Object,
                onboardingDirectivePublisher.Object,
                Mock.Of<ILogger<OnboardingGapDetectionScheduledJobHandler>>())
        };

        return new ScheduledJobExecutor(handlers, Mock.Of<ILogger<ScheduledJobExecutor>>());
    }
}
