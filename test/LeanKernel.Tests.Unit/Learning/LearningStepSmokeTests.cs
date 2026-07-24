using FluentAssertions;

using LeanKernel;
using LeanKernel.Events;
using LeanKernel.Services.Learning.Steps;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace LeanKernel.Tests.Unit.Learning;

public sealed class LearningStepSmokeTests
{
    [Fact]
    public async Task IdentityIntentExtractionStep_ExecuteAsync_Completes()
    {
        var step = new IdentityIntentExtractionStep(NullLogger<IdentityIntentExtractionStep>.Instance);

        var act = async () => await step.ExecuteAsync(CreateTurnEvent("I want to update my profile", "Done"));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CapabilityGapDetectionStep_ExecuteAsync_Completes()
    {
        var step = new CapabilityGapDetectionStep(NullLogger<CapabilityGapDetectionStep>.Instance);

        var act = async () => await step.ExecuteAsync(CreateTurnEvent("Can you please book this?", "I cannot do that yet."));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EngagementTrackingStep_ExecuteAsync_Completes()
    {
        var step = new EngagementTrackingStep(NullLogger<EngagementTrackingStep>.Instance);

        var act = async () => await step.ExecuteAsync(CreateTurnEvent("Thanks", "Happy to help"));
        await act.Should().NotThrowAsync();
    }

    private static TurnCompletedEvent CreateTurnEvent(string userMessage, string assistantResponse)
    {
        return new TurnCompletedEvent
        {
            Envelope = new EventEnvelope
            {
                EventType = "turn_completed",
                TenantId = Guid.NewGuid(),
                PersonId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                ChannelId = Guid.NewGuid(),
            },
            TurnId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            UserMessage = userMessage,
            AssistantResponse = assistantResponse,
            ElapsedMs = 250,
        };
    }
}
