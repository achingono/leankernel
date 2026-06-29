using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.Quality;

/// <summary>
/// Provides functionality for empty response check.
/// </summary>
public sealed class EmptyResponseCheck : IQualityCheck
{
    /// <summary>
    /// Gets name.
    /// </summary>
    public string Name => "empty-response";

    /// <summary>
    /// Gets order.
    /// </summary>
    public int Order => 0;

    /// <summary>
    /// Gets failure outcome.
    /// </summary>
    public QualityOutcome FailureOutcome => QualityOutcome.FailedEmpty;

    /// <summary>
    /// Executes evaluate.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The operation result.</returns>
    public QualityCheckResult Evaluate(QualityEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var passed = !string.IsNullOrWhiteSpace(context.Response);
        return new QualityCheckResult
        {
            CheckName = Name,
            Passed = passed,
            Score = passed ? 1.0 : 0.0,
            Details = passed ? null : "Response was empty or whitespace."
        };
    }
}
