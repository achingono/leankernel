using FluentAssertions;

using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Learning.Learning;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class LearningStepRunnerTests
{
    [Fact]
    public async Task ExecuteStepAsync_UnknownStep_ThrowsInvalidOperationException()
    {
        var runner = new LearningStepRunner([]);
        var turn = CreateTurnEvent();

        var act = () => runner.ExecuteStepAsync("unknown", turn, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unknown learning step*");
    }

    [Fact]
    public async Task ExecuteStepAsync_KnownStep_ExecutesMatchingStepOnly()
    {
        var targetStep = new Mock<ILearningPipelineStep>();
        targetStep.SetupGet(step => step.StepName).Returns("identity-intent");
        targetStep.Setup(step => step.ExecuteAsync(It.IsAny<CompletedTurnEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var otherStep = new Mock<ILearningPipelineStep>();
        otherStep.SetupGet(step => step.StepName).Returns("capability-gap");
        otherStep.Setup(step => step.ExecuteAsync(It.IsAny<CompletedTurnEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var runner = new LearningStepRunner([targetStep.Object, otherStep.Object]);
        var turn = CreateTurnEvent();

        await runner.ExecuteStepAsync("identity-intent", turn, CancellationToken.None);

        targetStep.Verify(step => step.ExecuteAsync(It.IsAny<CompletedTurnEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        otherStep.Verify(step => step.ExecuteAsync(It.IsAny<CompletedTurnEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CompletedTurnEvent CreateTurnEvent()
    {
        return new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-c",
            "turn-c",
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", "hello")],
            [new TurnMessage("assistant", "world")]);
    }
}
