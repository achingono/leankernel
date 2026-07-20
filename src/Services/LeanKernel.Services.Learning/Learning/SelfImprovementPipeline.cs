using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Runs all registered learning pipeline steps in registration order.
/// </summary>
/// <param name="steps">Ordered learning steps to execute per turn.</param>
public sealed class SelfImprovementPipeline(IEnumerable<ILearningPipelineStep> steps) : ISelfImprovementPipeline
{
    private readonly IReadOnlyList<ILearningPipelineStep> _steps = steps.ToList();

    public async Task ExecuteAsync(CompletedTurnEvent turnEvent, CancellationToken cancellationToken = default)
    {
        foreach (var step in _steps)
        {
            await step.ExecuteAsync(turnEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
    /// <inheritdoc />
