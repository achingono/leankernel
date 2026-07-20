using FluentAssertions;

using LeanKernel.Logic.Providers;
using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Learning.Learning;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class LearningPipelineStepTests
{
    [Fact]
    public async Task FactExtractionStep_WritesFacts_WhenAssistantResponseContainsSentences()
    {
        var coordinator = new Mock<IKnowledgePageUpdateCoordinator>();
        var step = new FactExtractionLearningStep(coordinator.Object);

        var turn = CreateTurnEvent(
            userText: "hello",
            assistantText: "Ada lives in London and likes tea. She has worked on kernels for ten years.");

        await step.ExecuteAsync(turn, CancellationToken.None);

        coordinator.Verify(
            candidate => candidate.WriteFactAsync(
                It.IsAny<CompletedTurnEvent>(),
                It.Is<string>(fact => fact.Contains("Ada lives", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task IdentityIntentStep_WritesIntent_WhenUserMessageContainsIdentitySignals()
    {
        var coordinator = new Mock<IKnowledgePageUpdateCoordinator>();
        var step = new IdentityIntentLearningStep(coordinator.Object);
        var turn = CreateTurnEvent(userText: "My name is Ada and I live in London.", assistantText: "Noted.");

        await step.ExecuteAsync(turn, CancellationToken.None);

        coordinator.Verify(
            candidate => candidate.WriteIdentityIntentAsync(
                It.IsAny<CompletedTurnEvent>(),
                It.Is<string>(value => value.Contains("name", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CapabilityGapStep_WritesGap_WhenAssistantIndicatesUnavailableCapability()
    {
        var coordinator = new Mock<IKnowledgePageUpdateCoordinator>();
        var step = new CapabilityGapLearningStep(coordinator.Object);
        var turn = CreateTurnEvent(userText: "Do X", assistantText: "I cannot access your calendar right now.");

        await step.ExecuteAsync(turn, CancellationToken.None);

        coordinator.Verify(
            candidate => candidate.WriteCapabilityGapAsync(
                It.IsAny<CompletedTurnEvent>(),
                It.Is<string>(value => value.Contains("cannot", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EngagementTrackingStep_WritesSignal()
    {
        var coordinator = new Mock<IKnowledgePageUpdateCoordinator>();
        var step = new EngagementTrackingLearningStep(coordinator.Object);
        var turn = CreateTurnEvent(userText: "hello", assistantText: "world");

        await step.ExecuteAsync(turn, CancellationToken.None);

        coordinator.Verify(
            candidate => candidate.WriteEngagementSignalAsync(
                It.IsAny<CompletedTurnEvent>(),
                It.Is<string>(signal => signal.Contains("request_chars=", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SelfImprovementPipeline_ExecutesStepsInRegistrationOrder()
    {
        var calls = new List<string>();

        var first = new Mock<ILearningPipelineStep>();
        first.SetupGet(step => step.StepName).Returns("first");
        first.Setup(step => step.ExecuteAsync(It.IsAny<CompletedTurnEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("first"))
            .Returns(Task.CompletedTask);

        var second = new Mock<ILearningPipelineStep>();
        second.SetupGet(step => step.StepName).Returns("second");
        second.Setup(step => step.ExecuteAsync(It.IsAny<CompletedTurnEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("second"))
            .Returns(Task.CompletedTask);

        var pipeline = new SelfImprovementPipeline([first.Object, second.Object]);

        await pipeline.ExecuteAsync(CreateTurnEvent("u", "a"), CancellationToken.None);

        calls.Should().Equal(["first", "second"]);
    }

    [Fact]
    public async Task SingleChannelMemoryPolicyResolver_ReturnsSourceChannelOnly()
    {
        var tenantId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var resolver = new SingleChannelMemoryPolicyResolver();

        var result = await resolver.ResolveAsync(tenantId, channelId, CancellationToken.None);

        result.TenantId.Should().Be(tenantId);
        result.ChannelId.Should().Be(channelId);
        result.ReadableChannelIds.Should().ContainSingle().Which.Should().Be(channelId);
        result.MutuallyVisibleChannelIds.Should().ContainSingle().Which.Should().Be(channelId);
    }

    private static CompletedTurnEvent CreateTurnEvent(string userText, string assistantText)
    {
        return new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-step",
            "turn-step",
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", userText)],
            [new TurnMessage("assistant", assistantText)]);
    }
}
