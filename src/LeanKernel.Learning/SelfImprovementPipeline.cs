using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Learning;

/// <summary>
/// Orchestrates the self-improvement learning pipeline by executing all registered
/// <see cref="ILearningStep"/> implementations in order against each turn event.
/// Steps are sorted by <see cref="ILearningStep.Order"/> then by name for deterministic execution.
/// </summary>
public sealed class SelfImprovementPipeline(
    IEnumerable<ILearningStep> steps,
    ILogger<SelfImprovementPipeline> logger) : ISelfImprovementPipeline
{
    private readonly IReadOnlyList<ILearningStep> _steps = (steps ?? throw new ArgumentNullException(nameof(steps)))
        .OrderBy(step => step.Order)
        .ThenBy(step => step.Name, StringComparer.Ordinal)
        .ToArray();
    private readonly ILogger<SelfImprovementPipeline> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Processes a turn event through all registered learning steps in sequential order.
    /// Each step is executed independently; failures in one step do not prevent subsequent steps from running.
    /// </summary>
    /// <param name="turnEvent">The turn event containing user message, assistant response, and context.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    public async Task ProcessTurnEventAsync(TurnEvent turnEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turnEvent);

        foreach (var step in _steps)
        {
            try
            {
                var result = await step.ProcessAsync(turnEvent, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Learning step {StepName} completed for session {SessionId} turn {TurnId}: success={Success} items={ItemsLearned}",
                    result.StepName,
                    turnEvent.SessionId,
                    turnEvent.TurnId,
                    result.Success,
                    result.ItemsLearned);

                if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
                {
                    _logger.LogWarning(
                        "Learning step {StepName} reported a recoverable error for session {SessionId} turn {TurnId}: {Error}",
                        result.StepName,
                        turnEvent.SessionId,
                        turnEvent.TurnId,
                        result.Error);
                }

                if (result.LearnedFacts.Count > 0)
                {
                    _logger.LogDebug(
                        "Learning step {StepName} learned facts for session {SessionId} turn {TurnId}: {Facts}",
                        result.StepName,
                        turnEvent.SessionId,
                        turnEvent.TurnId,
                        string.Join(" | ", result.LearnedFacts));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Learning step {StepName} failed for session {SessionId} turn {TurnId}",
                    step.Name,
                    turnEvent.SessionId,
                    turnEvent.TurnId);
            }
        }
    }
}
