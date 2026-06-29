using System.Text.Json;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Learning;

/// <summary>
/// Learning step that detects when the assistant indicates a knowledge or capability gap
/// (e.g., "I don't know", "I can't help with that") and records these gaps for future improvement.
/// Gaps are persisted as a JSON aggregate in the knowledge store.
/// </summary>
public sealed class CapabilityGapDetectionStep(
    IKnowledgeService knowledgeService,
    KnowledgePageUpdateCoordinator updateCoordinator,
    IOptions<LeanKernelConfig> config,
    ILogger<CapabilityGapDetectionStep> logger) : ILearningStep
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] DefaultPatterns =
    [
        "i don't know",
        "i do not know",
        "i'm not sure",
        "i am not sure",
        "i can't help with that",
        "i cannot help with that",
        "i'm unable to",
        "i am unable to"
    ];

    private readonly IKnowledgeService _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    private readonly KnowledgePageUpdateCoordinator _updateCoordinator = updateCoordinator ?? throw new ArgumentNullException(nameof(updateCoordinator));
    private readonly LeanKernelConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly ILogger<CapabilityGapDetectionStep> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public string Name => "capability-gap-detection";

    /// <inheritdoc/>
    public int Order => 20;

    /// <summary>
    /// Analyzes the assistant's response for known gap patterns and records any detected gaps.
    /// Existing gaps are updated with occurrence counts; new gaps are appended.
    /// </summary>
    /// <param name="turnEvent">The turn event to analyze for capability gaps.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="LearningStepResult"/> indicating whether a gap was detected.</returns>
    public Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turnEvent);

        if (!TryDetectGap(turnEvent, out var gap))
        {
            return Task.FromResult(new LearningStepResult
            {
                StepName = Name,
                Success = true,
                ItemsLearned = 0,
            });
        }

        return _updateCoordinator.ExecuteAsync(
            LearningKeys.CapabilityGapsPageKey,
            async cancellationToken =>
            {
                var gaps = await LoadGapsAsync(cancellationToken).ConfigureAwait(false);
                var existingIndex = gaps.FindIndex(candidate =>
                    string.Equals(candidate.Category, gap.Category, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.Description, gap.Description, StringComparison.OrdinalIgnoreCase));

                if (existingIndex >= 0)
                {
                    var existing = gaps[existingIndex];
                    gaps[existingIndex] = existing with
                    {
                        DetectedInSession = gap.DetectedInSession,
                        DetectedInTurn = gap.DetectedInTurn,
                        DetectedAt = gap.DetectedAt,
                        OccurrenceCount = existing.OccurrenceCount + 1,
                    };
                }
                else
                {
                    gaps.Add(gap);
                }

                var ordered = gaps
                    .OrderBy(candidate => candidate.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(candidate => candidate.Description, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                await _knowledgeService.PutPageAsync(
                    LearningKeys.CapabilityGapsPageKey,
                    JsonSerializer.Serialize(ordered, SerializerOptions),
                    cancellationToken).ConfigureAwait(false);

                _logger.LogDebug(
                    "Recorded capability gap {Category} for session {SessionId} turn {TurnId}",
                    gap.Category,
                    turnEvent.SessionId,
                    turnEvent.TurnId);

                return new LearningStepResult
                {
                    StepName = Name,
                    Success = true,
                    ItemsLearned = 1,
                    LearnedFacts = [gap.Description],
                };
            },
            ct);
    }

    /// <summary>
    /// Loads the existing capability gaps from the knowledge store.
    /// </summary>
    private async Task<List<CapabilityGap>> LoadGapsAsync(CancellationToken ct)
    {
        var page = await _knowledgeService.GetPageAsync(LearningKeys.CapabilityGapsPageKey, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(page?.Content))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CapabilityGap>>(page.Content, SerializerOptions) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse existing capability gap page; resetting aggregate state");
            return [];
        }
    }

    /// <summary>
    /// Attempts to detect a capability gap by matching the assistant's response against known patterns.
    /// </summary>
    private bool TryDetectGap(TurnEvent turnEvent, out CapabilityGap gap)
    {
        var response = turnEvent.AssistantResponse ?? turnEvent.Content;
        foreach (var pattern in EnumeratePatterns())
        {
            if (!response.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var category = pattern.Contains("know", StringComparison.OrdinalIgnoreCase) || pattern.Contains("sure", StringComparison.OrdinalIgnoreCase)
                ? "knowledge-gap"
                : "capability-gap";
            gap = new CapabilityGap
            {
                Category = category,
                Description = $"Assistant indicated a {category.Replace('-', ' ')} with pattern '{pattern}'.",
                DetectedInSession = turnEvent.SessionId,
                DetectedInTurn = turnEvent.TurnId,
                DetectedAt = turnEvent.Timestamp,
            };
            return true;
        }

        gap = null!;
        return false;
    }

    /// <summary>
    /// Enumerates all gap detection patterns from configuration and built-in defaults.
    /// </summary>
    private IEnumerable<string> EnumeratePatterns()
        => _config.Routing.RefusalPatterns
            .Concat(DefaultPatterns)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
