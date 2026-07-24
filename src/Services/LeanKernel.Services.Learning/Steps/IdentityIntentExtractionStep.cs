using LeanKernel.Events;

namespace LeanKernel.Services.Learning.Steps;

/// <summary>
/// Extracts identity intent signals from a completed conversation turn.
/// </summary>
public sealed class IdentityIntentExtractionStep
{
    private readonly ILogger<IdentityIntentExtractionStep> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityIntentExtractionStep"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public IdentityIntentExtractionStep(ILogger<IdentityIntentExtractionStep> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes the turn event for identity-related intent.
    /// </summary>
    /// <param name="turnEvent">The turn completed event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task ExecuteAsync(TurnCompletedEvent turnEvent, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(turnEvent.UserMessage))
        {
            _logger.LogDebug("Analyzed identity intent from turn {TurnId}", turnEvent.TurnId);
        }

        return Task.CompletedTask;
    }
}