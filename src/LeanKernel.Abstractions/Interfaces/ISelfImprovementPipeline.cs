using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Provides an interface for running self-improvement processes based on turn events.
/// </summary>
public interface ISelfImprovementPipeline
{
    /// <summary>
    /// Processes a turn event to update models, knowledge, or configuration.
    /// </summary>
    /// <param name="turnEvent">The turn event to process.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ProcessTurnEventAsync(TurnEvent turnEvent, CancellationToken ct = default);
}
