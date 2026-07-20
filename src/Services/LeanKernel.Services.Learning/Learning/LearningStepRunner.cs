using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Executes one named learning step from the registered pipeline steps.
/// </summary>
/// <param name="steps">Registered learning steps keyed by name.</param>
public sealed class LearningStepRunner(IEnumerable<ILearningPipelineStep> steps) : ILearningStepRunner
{
    private readonly IReadOnlyDictionary<string, ILearningPipelineStep> _steps = steps
        .ToDictionary(static step => step.StepName, StringComparer.OrdinalIgnoreCase);

    public async Task ExecuteStepAsync(string stepName, CompletedTurnEvent turnEvent, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepName);

        if (!_steps.TryGetValue(stepName, out var step))
        {
            var available = string.Join(", ", _steps.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase));
            throw new InvalidOperationException($"Unknown learning step '{stepName}'. Available steps: {available}.");
        }

        await step.ExecuteAsync(turnEvent, cancellationToken).ConfigureAwait(false);
    }
}
    /// <inheritdoc />
