using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Learning;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Learning;

public class EngagementTrackingStepTests
{
    [Fact]
    public async Task ProcessAsync_updates_topic_frequency_and_positive_signals()
    {
        var knowledgeService = new RecordingKnowledgeService();
        var step = new EngagementTrackingStep(
            knowledgeService,
            new KnowledgePageUpdateCoordinator(),
            NullLogger<EngagementTrackingStep>.Instance);

        var turnEvent = new TurnEvent
        {
            SessionId = "session-1",
            TurnId = "turn-1",
            Role = "assistant",
            Content = "Atlas release notes are ready.",
            UserMessage = "Thanks, can you summarize Atlas release notes?",
            AssistantResponse = "Atlas release notes are ready.",
            Context = new ConversationContext
            {
                SystemPrompt = "You are a helpful assistant.",
                RetrievedKnowledge =
                [
                    new RetrievalCandidate
                    {
                        Key = "atlas/release-notes",
                        Content = "Atlas release notes",
                        Source = "gbrain",
                        Score = 0.9,
                        TokenCount = 4
                    }
                ]
            }
        };

        var result = await step.ProcessAsync(turnEvent);

        result.Success.Should().BeTrue();
        knowledgeService.Pages.Should().ContainKey("learning/engagement-metrics");
        var metrics = JsonSerializer.Deserialize<EngagementMetrics>(knowledgeService.Pages["learning/engagement-metrics"], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        metrics.Should().NotBeNull();
        metrics!.TotalTurnsProcessed.Should().Be(1);
        metrics.PositiveSignals.Should().Be(1);
        metrics.TopicFrequency["notes"].Should().Be(1);
    }

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
