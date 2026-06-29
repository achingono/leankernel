using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.Quality;

/// <summary>
/// Provides functionality for response quality gate.
/// </summary>
public sealed class ResponseQualityGate : IResponseQualityGate
{
    private readonly IReadOnlyList<IQualityCheck> _checks;

    public ResponseQualityGate(
        EmptyResponseCheck emptyResponseCheck,
        MinLengthCheck minLengthCheck,
        RefusalDetectionCheck refusalDetectionCheck,
        ConstraintCoverageCheck constraintCoverageCheck)
        : this([emptyResponseCheck, minLengthCheck, refusalDetectionCheck, constraintCoverageCheck])
    {
    }

    internal ResponseQualityGate(IEnumerable<IQualityCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        _checks = checks.OrderBy(check => check.Order).ToArray();
    }

    /// <summary>
    /// Executes evaluate.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The operation result.</returns>
    public QualityGateResult Evaluate(QualityEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var checks = new List<QualityCheckResult>(_checks.Count);
        QualityOutcome outcome = QualityOutcome.Passed;
        string? failureReason = null;
        var foundFailure = false;

        foreach (var check in _checks)
        {
            var result = check.Evaluate(context);
            checks.Add(result);

            if (!foundFailure && !result.Passed)
            {
                outcome = check.FailureOutcome;
                failureReason = result.Details;
                foundFailure = true;
            }
        }

        return new QualityGateResult
        {
            Outcome = outcome,
            Passed = !foundFailure,
            Checks = checks,
            FailureReason = failureReason,
            OverallScore = checks.Count == 0 ? 1.0 : checks.Average(check => check.Score)
        };
    }
}
