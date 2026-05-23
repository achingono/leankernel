using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.Quality;

internal interface IQualityCheck
{
    string Name { get; }

    int Order { get; }

    QualityOutcome FailureOutcome { get; }

    QualityCheckResult Evaluate(QualityEvaluationContext context);
}
