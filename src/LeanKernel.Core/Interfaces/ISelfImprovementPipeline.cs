using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Coordinates post-turn learning steps for self-improvement.
/// </summary>
public interface ISelfImprovementPipeline
{
    /// <summary>
    /// Processes a completed turn through all configured learning steps.
    /// </summary>
    /// <param name="turnEvent">The completed turn event to learn from.</param>
    /// <param name="ct">A token used to cancel pipeline execution.</param>
    /// <returns>The aggregate self-improvement result.</returns>
    Task<SelfImprovementResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct);
}
