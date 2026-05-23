using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.Quality;

public sealed class MinLengthCheck : IQualityCheck
{
    public string Name => "min-length";

    public int Order => 1;

    public QualityOutcome FailureOutcome => QualityOutcome.FailedTooShort;

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
