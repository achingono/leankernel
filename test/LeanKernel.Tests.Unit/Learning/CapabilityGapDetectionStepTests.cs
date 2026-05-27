using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Learning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Learning;

public class CapabilityGapDetectionStepTests
{
    [Fact]
    public async Task ProcessAsync_detects_and_aggregates_capability_gaps()
    {
        var knowledgeService = new RecordingKnowledgeService();
        var step = new CapabilityGapDetectionStep(
            knowledgeService,
            new KnowledgePageUpdateCoordinator(),
            Options.Create(new LeanKernelConfig
            {
                Routing = new RoutingConfig
                {
                    RefusalPatterns = ["I cannot"]
                }
            }),
            NullLogger<CapabilityGapDetectionStep>.Instance);

        await step.ProcessAsync(CreateTurnEvent("turn-1", "I cannot complete that request right now."));
        var result = await step.ProcessAsync(CreateTurnEvent("turn-2", "I cannot complete that request right now."));

        result.ItemsLearned.Should().Be(1);
        knowledgeService.Pages.Should().ContainKey("learning/capability-gaps");
        var gaps = JsonSerializer.Deserialize<List<CapabilityGap>>(knowledgeService.Pages["learning/capability-gaps"], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        gaps.Should().ContainSingle();
        gaps![0].OccurrenceCount.Should().Be(2);
        gaps[0].DetectedInTurn.Should().Be("turn-2");
    }

    [Fact]
    public async Task ProcessAsync_returns_noop_when_no_gap_pattern_is_found()
    {
        var knowledgeService = new RecordingKnowledgeService();
        var step = new CapabilityGapDetectionStep(
            knowledgeService,
            new KnowledgePageUpdateCoordinator(),
            Options.Create(new LeanKernelConfig()),
            NullLogger<CapabilityGapDetectionStep>.Instance);

        var result = await step.ProcessAsync(CreateTurnEvent("turn-1", "Here is the complete answer."));

        result.ItemsLearned.Should().Be(0);
        knowledgeService.Pages.Should().BeEmpty();
    }

    private static TurnEvent CreateTurnEvent(string turnId, string assistantResponse)
        => new()
        {
            SessionId = "session-1",
            TurnId = turnId,
            Role = "assistant",
            Content = assistantResponse,
            UserMessage = "Can you do this?",
            AssistantResponse = assistantResponse,
        };

    private sealed class RecordingKnowledgeService : IKnowledgeService
    {
        public Dictionary<string, string> Pages { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalCandidate>>([]);

        public Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
            => Task.FromResult<KnowledgePage?>(Pages.TryGetValue(key, out var content)
                ? new KnowledgePage { Key = key, Content = content }
                : null);

        public Task PutPageAsync(string key, string content, CancellationToken ct = default)
        {
            Pages[key] = content;
            return Task.CompletedTask;
        }

        public Task DeletePageAsync(string key, CancellationToken ct = default)
        {
            Pages.Remove(key);
            return Task.CompletedTask;
        }
    }
}
