using System.Text.Json;

using FluentAssertions;

using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Common.Scheduler;
using LeanKernel.Services.Learning.Learning;
using LeanKernel.Services.Learning.Scheduler;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class JobHandlerBehaviorTests
{
    [Fact]
    public async Task PingHandler_HandlesMissingMessagePayload()
    {
        var handler = new PingScheduledJobHandler(Mock.Of<ILogger<PingScheduledJobHandler>>());
        var job = new ScheduledJobDefinition { Name = "ping", JobType = ScheduledJobTypes.LearningPing };

        var act = () => handler.ExecuteAsync(job, JsonDocument.Parse("{}").RootElement.Clone(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReplayTurnHandler_MissingPayload_Throws()
    {
        var pipeline = new Mock<ISelfImprovementPipeline>();
        var handler = new ReplayTurnScheduledJobHandler(pipeline.Object, Mock.Of<ILogger<ReplayTurnScheduledJobHandler>>());
        var job = new ScheduledJobDefinition { Name = "replay", JobType = ScheduledJobTypes.LearningReplayTurn };

        var act = () => handler.ExecuteAsync(job, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires a JSON payload*");
    }

    [Fact]
    public async Task OnboardingGapHandler_NoGaps_DoesNotPublish()
    {
        var detector = new Mock<IOnboardingGapDetector>();
        var builder = new Mock<IOnboardingDirectiveBuilder>();
        var publisher = new Mock<IOnboardingDirectivePublisher>();
        detector.Setup(candidate => candidate.DetectGaps(It.IsAny<CompletedTurnEvent>())).Returns([]);

        var handler = new OnboardingGapDetectionScheduledJobHandler(
            detector.Object,
            builder.Object,
            publisher.Object,
            Mock.Of<ILogger<OnboardingGapDetectionScheduledJobHandler>>());

        var job = new ScheduledJobDefinition { Name = "gap", JobType = ScheduledJobTypes.OnboardingDetectGaps };
        var turn = CreateTurnEvent();
        var payload = JsonSerializer.SerializeToElement(turn);

        await handler.ExecuteAsync(job, payload, CancellationToken.None);

        publisher.Verify(candidate => candidate.PublishAsync(It.IsAny<CompletedTurnEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CompletedTurnEvent CreateTurnEvent()
    {
        return new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-handler",
            "turn-handler",
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", "hello")],
            [new TurnMessage("assistant", "world")]);
    }
}
