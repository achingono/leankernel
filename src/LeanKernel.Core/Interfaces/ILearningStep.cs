using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Performs one post-turn learning action.
/// </summary>
public interface ILearningStep
{
    /// <summary>
    /// Gets the stable learning step name used for logging and configuration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Processes a turn event.
    /// </summary>
    /// <param name="turnEvent">The completed turn event to learn from.</param>
    /// <param name="ct">A token used to cancel learning.</param>
    /// <returns>The learning step result.</returns>
    Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct);
}
