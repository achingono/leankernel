using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Defines an interface for verifying the quality of an agent response.
/// </summary>
public interface IResponseQualityGate
{
    /// <summary>
    /// Evaluates a response based on the provided context.
    /// </summary>
    /// <param name="context">The evaluation context.</param>
    /// <returns>The result of the quality evaluation.</returns>
    QualityGateResult Evaluate(QualityEvaluationContext context);
}
