using System.Text.Json;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Learning;

public sealed class EngagementTrackingStep(
    IKnowledgeService knowledgeService,
    KnowledgePageUpdateCoordinator updateCoordinator,
    ILogger<EngagementTrackingStep> logger) : ILearningStep
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] PositiveSignals = ["thanks", "thank you", "great", "awesome", "perfect", "helpful"];
    private static readonly string[] NegativeSignals = ["not helpful", "doesn't work", "does not work", "wrong", "frustrated", "still broken"];
    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "with", "that", "this", "from", "your", "have", "about", "into", "what", "when", "where", "please", "could", "would", "there", "their", "them", "then"
    ];

    private readonly IKnowledgeService _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    private readonly KnowledgePageUpdateCoordinator _updateCoordinator = updateCoordinator ?? throw new ArgumentNullException(nameof(updateCoordinator));
    private readonly ILogger<EngagementTrackingStep> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string Name => "engagement-tracking";

    public int Order => 30;

    public Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turnEvent);

        return _updateCoordinator.ExecuteAsync(
            LearningKeys.EngagementMetricsPageKey,
            async cancellationToken =>
            {
                var metrics = await LoadMetricsAsync(cancellationToken).ConfigureAwait(false);
                metrics.TotalTurnsProcessed++;
                metrics.LastUpdated = turnEvent.Timestamp;

                foreach (var topic in ExtractTopics(turnEvent))
                {
                    metrics.TopicFrequency[topic] = metrics.TopicFrequency.TryGetValue(topic, out var count)
                        ? count + 1
                        : 1;
                }

                var userText = turnEvent.UserMessage ?? string.Empty;
                if (PositiveSignals.Any(signal => userText.Contains(signal, StringComparison.OrdinalIgnoreCase)))
                {
                    metrics.PositiveSignals++;
                }

                if (NegativeSignals.Any(signal => userText.Contains(signal, StringComparison.OrdinalIgnoreCase)))
                {
                    metrics.NegativeSignals++;
                }

                await _knowledgeService.PutPageAsync(
                    LearningKeys.EngagementMetricsPageKey,
                    JsonSerializer.Serialize(metrics, SerializerOptions),
                    cancellationToken).ConfigureAwait(false);

                var itemsLearned = metrics.TopicFrequency.Count > 0 ? 1 : 0;
                _logger.LogDebug(
                    "Updated engagement metrics for session {SessionId} turn {TurnId}",
                    turnEvent.SessionId,
                    turnEvent.TurnId);

                return new LearningStepResult
                {
                    StepName = Name,
                    Success = true,
                    ItemsLearned = itemsLearned,
                    LearnedFacts = metrics.TopicFrequency.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                };
            },
            ct);
    }

    private async Task<EngagementMetrics> LoadMetricsAsync(CancellationToken ct)
    {
        var page = await _knowledgeService.GetPageAsync(LearningKeys.EngagementMetricsPageKey, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(page?.Content))
        {
            return new EngagementMetrics();
        }

        try
        {
            return JsonSerializer.Deserialize<EngagementMetrics>(page.Content, SerializerOptions) ?? new EngagementMetrics();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse existing engagement metrics page; resetting aggregate state");
            return new EngagementMetrics();
        }
    }

    private static IReadOnlyList<string> ExtractTopics(TurnEvent turnEvent)
    {
        var topics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in turnEvent.Context?.RetrievedKnowledge ?? [])
        {
            var rawTopic = candidate.Key
                .Split(['/', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            if (!string.IsNullOrWhiteSpace(rawTopic))
            {
                topics.Add(rawTopic.ToLowerInvariant());
            }
        }

        if (topics.Count > 0)
        {
            return topics.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        foreach (var token in (turnEvent.UserMessage ?? string.Empty)
                     .Split([' ', '\r', '\n', '\t', ',', '.', '?', '!', ':', ';', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = token.Trim().ToLowerInvariant();
            if (normalized.Length < 4 || StopWords.Contains(normalized))
            {
                continue;
            }

            topics.Add(normalized);
            if (topics.Count >= 3)
            {
                break;
            }
        }

        return topics.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public sealed class EngagementMetrics
{
    public int TotalTurnsProcessed { get; set; }

    public int PositiveSignals { get; set; }

    public int NegativeSignals { get; set; }

    public Dictionary<string, int> TopicFrequency { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
