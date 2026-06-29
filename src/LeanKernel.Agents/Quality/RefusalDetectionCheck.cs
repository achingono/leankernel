using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Quality;

/// <summary>
/// Provides functionality for refusal detection check.
/// </summary>
public sealed class RefusalDetectionCheck : IQualityCheck
{
    private readonly string[] _patterns;

    public RefusalDetectionCheck(IOptions<LeanKernelConfig> config)
        : this(config?.Value.Routing.RefusalPatterns)
    {
    }

    private RefusalDetectionCheck(IEnumerable<string>? refusalPatterns)
    {
        _patterns = refusalPatterns?
            .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(static pattern => pattern.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    /// <summary>
    /// Gets name.
    /// </summary>
    public string Name => "refusal-detection";

    /// <summary>
    /// Gets order.
    /// </summary>
    public int Order => 2;

    /// <summary>
    /// Gets failure outcome.
    /// </summary>
    public QualityOutcome FailureOutcome => QualityOutcome.FailedRefusal;

    /// <summary>
    /// Executes evaluate.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The operation result.</returns>
    public QualityCheckResult Evaluate(QualityEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var matchedPattern = _patterns.FirstOrDefault(pattern =>
            context.Response.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        var passed = matchedPattern is null;

        return new QualityCheckResult
        {
            CheckName = Name,
            Passed = passed,
            Score = passed ? 1.0 : 0.0,
            Details = passed ? null : $"Matched refusal pattern '{matchedPattern}'."
        };
    }
}
