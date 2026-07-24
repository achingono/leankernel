using LeanKernel.Events;

namespace LeanKernel.Services.Learning.Steps;

/// <summary>
/// Detects capability gaps from user requests in a completed conversation turn.
/// </summary>
public sealed class CapabilityGapDetectionStep
{
    private readonly ILogger<CapabilityGapDetectionStep> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CapabilityGapDetectionStep"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public CapabilityGapDetectionStep(ILogger<CapabilityGapDetectionStep> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes the turn event for potential capability gaps.
    /// </summary>
    /// <param name="turnEvent">The turn completed event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task ExecuteAsync(TurnCompletedEvent turnEvent, CancellationToken ct = default)
    {
        var needsTool = turnEvent.UserMessage?.Contains("can you", StringComparison.OrdinalIgnoreCase) == true
                        || turnEvent.UserMessage?.Contains("please", StringComparison.OrdinalIgnoreCase) == true;

        if (needsTool)
        {
            _logger.LogDebug("Potential capability gap detected in turn {TurnId}", turnEvent.TurnId);
        }

        return Task.CompletedTask;
    }
}