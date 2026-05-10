using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Thinker.Routing;

/// <summary>
/// Validates a response against deterministic quality checks (FR-4).
/// If any check fails, the caller should retry with the next candidate.
/// </summary>
public sealed class ResponseQualityGate
{
    private readonly RoutingConfig _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResponseQualityGate" /> class.
    /// </summary>
    /// <param name="config">The config.</param>
    public ResponseQualityGate(IOptions<LeanKernelConfig> config)
    {
        _config = config.Value.Routing;
    }

    /// <summary>
    /// Returns <c>true</c> when the response passes all quality checks.
    /// </summary>
    public bool Passes(string response, string prompt, int constraintCount, out string? failReason)
    {
        var trimmed = response.Trim();

        // Check 1: non-empty output.
        if (trimmed.Length == 0)
        {
            failReason = "empty_output";
            return false;
        }

        // Check 2: minimum useful output length (skip for explicitly terse prompts).
        if (!IsTersePrompt(prompt) && trimmed.Length < _config.QualityMinOutputLength)
        {
            failReason = $"output_too_short:{trimmed.Length}<{_config.QualityMinOutputLength}";
            return false;
        }

        // Check 3: constraint coverage for medium/large requests (FR-4).
        if (constraintCount >= 4)
        {
            var coverage = EstimateConstraintCoverage(prompt, trimmed, constraintCount);
            if (coverage < _config.QualityMinConstraintCoverage)
            {
                failReason = $"low_constraint_coverage:{coverage:F2}<{_config.QualityMinConstraintCoverage:F2}";
                return false;
            }
        }

        failReason = null;
        return true;
    }

    /// <summary>
    /// Heuristic: prompt asks for a very short answer.
    /// </summary>
    private static bool IsTersePrompt(string prompt)
    {
        var lower = prompt.ToLowerInvariant();
        return lower.Contains("one word") ||
               lower.Contains("one sentence") ||
               lower.Contains("yes or no") ||
               lower.Contains("single word") ||
               lower.Contains("briefly");
    }

    /// <summary>
    /// Rough coverage estimate: fraction of numbered/bulleted constraint items from the
    /// prompt that have a corresponding item or keyword in the response.
    /// </summary>
    private static double EstimateConstraintCoverage(string prompt, string response, int constraintCount)
    {
        if (constraintCount == 0)
            return 1.0;

        // Extract the key noun/verb tokens from the prompt (skip stop words).
        var promptWords = ExtractKeyWords(prompt);
        if (promptWords.Count == 0)
            return 1.0;

        var responseLower = response.ToLowerInvariant();
        var covered = promptWords.Count(w => responseLower.Contains(w));
        return (double)covered / promptWords.Count;
    }

    private static List<string> ExtractKeyWords(string text)
    {
        // Very lightweight: lowercase, split, drop short/common words.
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "is", "are", "was", "be", "to", "of", "and",
            "or", "in", "on", "at", "for", "with", "by", "as", "it", "do"
        };

        return text.ToLowerInvariant()
            .Split((char[])[], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3 && !stop.Contains(w))
            .Distinct()
            .Take(40) // cap to keep it fast
            .ToList();
    }
}
