using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Learning;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Learning;

public class SelfImprovementPipelineTests
{
    [Fact]
    public async Task ProcessTurnEventAsync_runs_steps_in_order_and_swallows_failures()
    {
        var executed = new List<string>();
        var pipeline = new SelfImprovementPipeline(
        [
            new TestLearningStep("third", 30, executed),
            new ThrowingLearningStep("second", 20, executed),
            new TestLearningStep("first", 10, executed),
            new TestLearningStep("fourth", 40, executed)
        ],
            NullLogger<SelfImprovementPipeline>.Instance);

        var act = () => pipeline.ProcessTurnEventAsync(CreateTurnEvent());

        await act.Should().NotThrowAsync();
        executed.Should().Equal("first", "second", "third", "fourth");
    }

    private static TurnEvent CreateTurnEvent()
        => new()
        {
            SessionId = "session-1",
            TurnId = "turn-1",
            Role = "assistant",
            Content = "Assistant response",
            UserMessage = "User message",
            AssistantResponse = "Assistant response",
        };

    private sealed class TestLearningStep(string name, int order, List<string> executed) : ILearningStep
    {
        public string Name { get; } = name;

        public int Order { get; } = order;

        public Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct = default)
        {
            executed.Add(Name);
            return Task.FromResult(new LearningStepResult
            {
                StepName = Name,
                Success = true,
                ItemsLearned = 1,
            });
        }
    }

    private sealed class ThrowingLearningStep(string name, int order, List<string> executed) : ILearningStep
    {
        public string Name { get; } = name;

        public int Order { get; } = order;

        public Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct = default)
        {
            executed.Add(Name);
            throw new InvalidOperationException("boom");
        }
    }
}
