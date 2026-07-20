using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Represents one ordered stage in the self-improvement learning pipeline.
/// </summary>
public interface ILearningPipelineStep
{
    /// <summary>
    /// Gets the unique step name.
    /// </summary>
    string StepName { get; }

    /// <summary>
    /// Executes the learning step for one completed turn.
    /// </summary>
    Task ExecuteAsync(CompletedTurnEvent turnEvent, CancellationToken cancellationToken = default);
}
