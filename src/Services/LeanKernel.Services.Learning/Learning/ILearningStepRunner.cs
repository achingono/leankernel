using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Executes a single named learning step on demand.
/// </summary>
public interface ILearningStepRunner
{
    /// <summary>
    /// Executes the specified step for the provided completed turn.
    /// </summary>
    Task ExecuteStepAsync(string stepName, CompletedTurnEvent turnEvent, CancellationToken cancellationToken = default);
}
