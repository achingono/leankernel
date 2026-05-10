using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Resources;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Executes configured post-turn learning steps in registration order.
/// </summary>
public sealed class SelfImprovementPipeline : ISelfImprovementPipeline
{
    private readonly IReadOnlyList<ILearningStep> _steps;
    private readonly ILogger<SelfImprovementPipeline> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SelfImprovementPipeline" /> class.
    /// </summary>
    /// <param name="steps">The learning steps to run in order.</param>
    /// <param name="logger">The logger used for pipeline diagnostics.</param>
    public SelfImprovementPipeline(
        IEnumerable<ILearningStep> steps,
        ILogger<SelfImprovementPipeline> logger)
    {
        _steps = steps.ToList();
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SelfImprovementResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct)
    {
        var results = new List<LearningStepResult>(_steps.Count);

        foreach (var step in _steps)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                results.Add(await step.ProcessAsync(turnEvent, ct));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ResourceText.Log("LearningStepFailed"),
                    step.Name, turnEvent.Id);
                results.Add(LearningStepResult.Failed(step.Name, ex.Message));
            }
        }

        return new SelfImprovementResult
        {
            TurnEventId = turnEvent.Id,
            StepResults = results
        };
    }
}
