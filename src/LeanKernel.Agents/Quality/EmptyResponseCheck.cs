using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.Quality;

public sealed class EmptyResponseCheck : IQualityCheck
{
    public string Name => "empty-response";

    public int Order => 0;

    public QualityOutcome FailureOutcome => QualityOutcome.FailedEmpty;

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
