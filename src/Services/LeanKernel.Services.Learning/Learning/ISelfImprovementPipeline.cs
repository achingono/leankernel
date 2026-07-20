using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Executes the ordered learning pipeline for completed turns.
/// </summary>
public interface ISelfImprovementPipeline
{
    /// <summary>
    /// Runs all configured learning steps for the supplied turn.
    /// </summary>
    Task ExecuteAsync(CompletedTurnEvent turnEvent, CancellationToken cancellationToken = default);
}
