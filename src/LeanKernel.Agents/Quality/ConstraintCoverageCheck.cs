using System.Text.RegularExpressions;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.Quality;

/// <summary>
/// Provides functionality for constraint coverage check.
/// </summary>
public sealed class ConstraintCoverageCheck : IQualityCheck
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "for", "from", "how", "if", "in", "into", "is", "it", "of", "on", "or", "s", "such", "that", "the", "their", "then", "there", "these", "they", "this", "to", "was", "will", "with", "you", "your"
    };

    private static readonly Regex ConstraintWordPattern = new(@"\b[a-zA-Z][a-zA-Z0-9_-]{2,}\b", RegexOptions.Compiled);

    /// <summary>
    /// Gets name.
    /// </summary>
    public string Name => "constraint-coverage";

    /// <summary>
    /// Gets order.
    /// </summary>
    public int Order => 3;

    /// <summary>
    /// Gets failure outcome.
    /// </summary>
    public QualityOutcome FailureOutcome => QualityOutcome.FailedLowCoverage;

    /// <summary>
    /// Executes evaluate.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The operation result.</returns>
    public QualityCheckResult Evaluate(QualityEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var constraints = GetConstraints(context).ToArray();
        if (constraints.Length == 0)
        {
            return new QualityCheckResult
            {
                CheckName = Name,
                Passed = true,
                Score = 1.0,
                Details = "No expected constraints were provided or derived."
            };
        }

        var normalizedResponse = NormalizeText(context.Response);
        var responseTerms = ExtractTerms(context.Response).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = constraints.Count(constraint => ContainsConstraint(normalizedResponse, responseTerms, constraint));
        var coverage = matches / (double)constraints.Length;
        var passed = coverage >= context.MinConstraintCoverage;

        return new QualityCheckResult
        {
            CheckName = Name,
            Passed = passed,
            Score = coverage,
            Details = $"Matched {matches} of {constraints.Length} constraints ({coverage:0.00})."
        };
    }

    private static IEnumerable<string> GetConstraints(QualityEvaluationContext context)
    {
        if (context.ExpectedConstraints is { Count: > 0 })
        {
            return context.ExpectedConstraints
                .Select(NormalizeText)
                .Where(static constraint => !string.IsNullOrWhiteSpace(constraint))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        return ExtractTerms(context.UserMessage)
            .Where(term => !StopWords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8);
    }

    private static IEnumerable<string> ExtractTerms(string value)
        => ConstraintWordPattern.Matches(value)
            .Select(static match => match.Value.ToLowerInvariant());

    private static string NormalizeText(string value)
        => string.Join(' ', ExtractTerms(value));

    private static bool ContainsConstraint(string normalizedResponse, IReadOnlySet<string> responseTerms, string constraint)
    {
        if (constraint.Contains(' ', StringComparison.Ordinal))
        {
            return normalizedResponse.Contains(constraint, StringComparison.Ordinal);
        }

        return responseTerms.Contains(constraint);
    }
}
