using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;

namespace LeanKernel.Thinker.Routing;

/// <summary>
/// Classifies each request into a complexity tier (small / medium / large) based on
/// estimated token count and constraint count (FR-1).
/// </summary>
public sealed class TaskComplexityScorer
{
    // Rough words-to-tokens ratio used by most LLM tokenizers.
    private const double WordsPerToken = 0.75;

    // Patterns that detect enumerated / explicit constraints in the prompt (FR-1).
    private static readonly Regex[] ConstraintPatterns =
    [
        // Numbered items: "1.", "2.", "1)", etc.
        new(@"^\s*\d+[\.\)]\s+\S", RegexOptions.Multiline | RegexOptions.Compiled),
        // Bullet/dash items: "- do X", "* do Y"
        new(@"^\s*[-*•]\s+\S", RegexOptions.Multiline | RegexOptions.Compiled),
        // Explicit format requirements
        new(@"\b(must|should|require[ds]?|output as|format as|return as|json|xml|csv|markdown)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Tool/model/provider requirements
        new(@"\b(use|using|with|via|call)\s+(tool|model|provider|api|function)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private readonly RoutingConfig _config;

    public TaskComplexityScorer(IOptions<LeanKernelConfig> config)
    {
        _config = config.Value.Routing;
    }

    /// <summary>
    /// Returns the complexity tier and a breakdown of the scoring decision.
    /// </summary>
    public (TaskComplexity Complexity, int EstimatedTokens, int ConstraintCount) Score(
        string prompt,
        int existingContextTokens = 0)
    {
        var tokens = EstimateTokens(prompt) + existingContextTokens;
        var constraints = CountConstraints(prompt);

        var complexity = Classify(tokens, constraints);
        return (complexity, tokens, constraints);
    }

    private TaskComplexity Classify(int tokens, int constraints)
    {
        // Large: exceeds medium thresholds on either dimension.
        if (tokens > _config.MediumMaxTokens || constraints > _config.MediumMaxConstraints)
            return TaskComplexity.Large;

        // Medium: exceeds small thresholds.
        if (tokens > _config.SmallMaxTokens || constraints > _config.SmallMaxConstraints)
            return TaskComplexity.Medium;

        return TaskComplexity.Small;
    }

    internal static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // Approximate: split on whitespace, divide by words-per-token ratio.
        var wordCount = text.Split((char[])[], StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)Math.Ceiling(wordCount / WordsPerToken);
    }

    internal static int CountConstraints(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var total = 0;
        foreach (var pattern in ConstraintPatterns)
            total += pattern.Matches(text).Count;

        return total;
    }
}
