using System.Text.Json;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Learning;

/// <summary>
/// Learning step that tracks user engagement metrics including topic frequency,
/// positive signals, and negative signals from conversation turns.
/// Metrics are persisted as a JSON aggregate in the knowledge store.
/// </summary>
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

    /// <inheritdoc/>
    public string Name => "engagement-tracking";

    /// <inheritdoc/>
    public int Order => 30;

    /// <summary>
    /// Processes a turn event to extract topics, detect engagement signals, and update aggregate metrics.
    /// </summary>
    /// <param name="turnEvent">The turn event to track engagement for.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="LearningStepResult"/> indicating success and the topics tracked.</returns>
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

    /// <summary>
    /// Loads the existing engagement metrics from the knowledge store.
    /// </summary>
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

    /// <summary>
    /// Extracts topic keywords from the turn event using retrieved knowledge keys or user message tokens.
    /// </summary>
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

/// <summary>
/// Aggregate metrics for user engagement tracking across conversation turns.
/// </summary>
public sealed class EngagementMetrics
{
    /// <summary>
    /// Gets or sets the total number of turns processed.
    /// </summary>
    public int TotalTurnsProcessed { get; set; }

    /// <summary>
    /// Gets or sets the count of positive engagement signals detected.
    /// </summary>
    public int PositiveSignals { get; set; }

    /// <summary>
    /// Gets or sets the count of negative engagement signals detected.
    /// </summary>
    public int NegativeSignals { get; set; }

    /// <summary>
    /// Gets or sets the frequency map of topics encountered across turns.
    /// </summary>
    public Dictionary<string, int> TopicFrequency { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the timestamp of the last processed turn.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
