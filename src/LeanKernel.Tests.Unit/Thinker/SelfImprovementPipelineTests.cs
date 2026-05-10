using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Services;

namespace LeanKernel.Tests.Unit.Thinker;

public sealed class SelfImprovementPipelineTests
{
    [Fact]
    public async Task ProcessAsync_RunsStepsInRegistrationOrder()
    {
        var order = new List<string>();
        var pipeline = new SelfImprovementPipeline(
            [
                new RecordingStep("first", order),
                new RecordingStep("second", order)
            ],
            NullLogger<SelfImprovementPipeline>.Instance);

        var result = await pipeline.ProcessAsync(CreateTurnEvent(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public async Task ProcessAsync_RecordsFailedStepAndContinues()
    {
        var order = new List<string>();
        var pipeline = new SelfImprovementPipeline(
            [
                new ThrowingStep("broken"),
                new RecordingStep("after", order)
            ],
            NullLogger<SelfImprovementPipeline>.Instance);

        var result = await pipeline.ProcessAsync(CreateTurnEvent(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.StepResults, r => r.StepName == "broken" && !r.Success);
        Assert.Equal(["after"], order);
    }

    [Fact]
    public async Task TurnEventQueue_RestoresPendingEventsFromDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Create(new LeanKernelConfig
            {
                Wiki = new WikiConfig { BasePath = Path.Combine(root, "wiki") },
                SelfImprovement = new SelfImprovementConfig { QueuePath = "queue/learning" }
            });
            var turnEvent = CreateTurnEvent();
            var writer = new TurnEventQueue(options, NullLogger<TurnEventQueue>.Instance);
            await writer.EnqueueAsync(turnEvent, CancellationToken.None);

            var reader = new TurnEventQueue(options, NullLogger<TurnEventQueue>.Instance);
            await reader.RestorePendingAsync(CancellationToken.None);

            await foreach (var restored in reader.ReadAllAsync(CancellationToken.None))
            {
                Assert.Equal(turnEvent.Id, restored.Id);
                await reader.MarkProcessedAsync(restored.Id, CancellationToken.None);
                break;
            }

            Assert.False(File.Exists(Path.Combine(root, "queue", "learning", $"{turnEvent.Id}.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static TurnEvent CreateTurnEvent() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionId = "session-1",
        UserMessage = new LeanKernelMessage
        {
            Id = "message-1",
            ChannelId = "test",
            SenderId = "user",
            Content = "My name is Alex.",
            Timestamp = DateTimeOffset.UtcNow
        },
        AssistantResponse = "Nice to meet you.",
        SourceId = "conversation:test"
    };

    private sealed class RecordingStep : ILearningStep
    {
        private readonly List<string> _order;

        public RecordingStep(string name, List<string> order)
        {
            Name = name;
            _order = order;
        }

        public string Name { get; }

        public Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct)
        {
            _order.Add(Name);
            return Task.FromResult(LearningStepResult.Succeeded(Name));
        }
    }

    private sealed class ThrowingStep : ILearningStep
    {
        public ThrowingStep(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct) =>
            throw new InvalidOperationException("step failed");
    }
}
