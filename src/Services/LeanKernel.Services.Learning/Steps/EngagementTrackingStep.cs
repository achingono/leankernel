using LeanKernel.Events;

namespace LeanKernel.Services.Learning.Steps;

/// <summary>
/// Tracks user engagement signals from a completed conversation turn.
/// </summary>
public sealed class EngagementTrackingStep
{
    private readonly ILogger<EngagementTrackingStep> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EngagementTrackingStep"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public EngagementTrackingStep(ILogger<EngagementTrackingStep> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes the turn event for engagement signals.
    /// </summary>
    /// <param name="turnEvent">The turn completed event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task ExecuteAsync(TurnCompletedEvent turnEvent, CancellationToken ct = default)
    {
        var hasFollowUp = !string.IsNullOrWhiteSpace(turnEvent.UserMessage);
        var hasResponse = !string.IsNullOrWhiteSpace(turnEvent.AssistantResponse);

        if (hasFollowUp && hasResponse)
        {
            _logger.LogDebug("Engagement signal for turn {TurnId}: {ElapsedMs}ms", turnEvent.TurnId, turnEvent.ElapsedMs);
        }

        return Task.CompletedTask;
    }
}