using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Represents a step in the self-improvement learning pipeline.
/// </summary>
public interface ILearningStep
{
    /// <summary>
    /// Gets the name of the learning step.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the order of the learning step in the pipeline.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Executes the learning step.
    /// </summary>
    /// <param name="turnEvent">The turn event to process.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct = default);
}

/// <summary>
/// Represents the result of a learning step.
/// </summary>
public sealed record LearningStepResult
{
    /// <summary>
    /// Gets the name of the step that was executed.
    /// </summary>
    public required string StepName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the learning step was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the number of new items learned during the step.
    /// </summary>
    public int ItemsLearned { get; init; }

    /// <summary>
    /// Gets the error message if the step failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets the list of newly learned facts.
    /// </summary>
    public IReadOnlyList<string> LearnedFacts { get; init; } = [];
}
