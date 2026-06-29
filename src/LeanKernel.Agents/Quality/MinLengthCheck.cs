using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.Quality;

/// <summary>
/// Provides functionality for min length check.
/// </summary>
public sealed class MinLengthCheck : IQualityCheck
{
    /// <summary>
    /// Gets name.
    /// </summary>
    public string Name => "min-length";

    /// <summary>
    /// Gets order.
    /// </summary>
    public int Order => 1;

    /// <summary>
    /// Gets failure outcome.
    /// </summary>
    public QualityOutcome FailureOutcome => QualityOutcome.FailedTooShort;

    /// <summary>
    /// Executes evaluate.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The operation result.</returns>
    public QualityCheckResult Evaluate(QualityEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.MinOutputLength <= 0)
        {
            return new QualityCheckResult
            {
                CheckName = Name,
                Passed = true,
                Score = 1.0
            };
        }

        var responseLength = context.Response.Length;
        var passed = responseLength >= context.MinOutputLength;
        var score = passed
            ? 1.0
            : Math.Clamp(responseLength / (double)context.MinOutputLength, 0.0, 1.0);

        return new QualityCheckResult
        {
            CheckName = Name,
            Passed = passed,
            Score = score,
            Details = passed
                ? null
                : $"Response length {responseLength} is below minimum {context.MinOutputLength}."
        };
    }
}
