using System.Text;
using System.Text.RegularExpressions;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Enhancement;

internal static partial class ResponseFormatHeuristics
{
    private static readonly string[] MathIntentTerms =
    [
        "math",
        "mathematics",
        "latex",
        "equation",
        "proof",
        "solve",
        "integral",
        "derivative",
        "algebra",
        "geometry",
        "trigonometry",
        "calculate",
        "calculation"
    ];

    public static bool ContainsBoxedMath(string text) =>
        !string.IsNullOrWhiteSpace(text) && BoxedExpressionRegex().IsMatch(text);

    public static bool ContainsExamWrapper(string text) =>
        !string.IsNullOrWhiteSpace(text) && ExamWrapperRegex().IsMatch(text);

    public static bool IsMathContext(string userQuery, ConversationContext context)
    {
        if (!string.IsNullOrWhiteSpace(userQuery))
        {
            var lowered = userQuery.ToLowerInvariant();
            if (MathIntentTerms.Any(term => lowered.Contains(term, StringComparison.Ordinal)))
            {
                return true;
            }

            if (lowered.Contains("without using math", StringComparison.Ordinal) ||
                lowered.Contains("no latex", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return context.ActiveToolNames.Any(name =>
            name.Contains("math", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("calculator", StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeNonMathArtifacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var segments = text.Split("```");
        if (segments.Length == 1)
        {
            return NormalizeSegment(text);
        }

        var sb = new StringBuilder();
        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
            {
                sb.Append("```");
            }

            // Odd segments are fenced code blocks.
            sb.Append(i % 2 == 1 ? segments[i] : NormalizeSegment(segments[i]));
        }

        return sb.ToString();
    }

    private static string NormalizeSegment(string input)
    {
        var withoutWrapper = ExamWrapperRegex().Replace(input, string.Empty);
        var unboxed = BoxedCaptureRegex().Replace(withoutWrapper, match => match.Groups["value"].Value.Trim());
        return unboxed;
    }

    [GeneratedRegex(@"(?im)^\s*(?:therefore,\s*)?(?:the\s+)?(?:final\s+)?answer\s+is\s*:\s*")]
    private static partial Regex ExamWrapperRegex();

    [GeneratedRegex(@"\$\s*\\boxed\{(?<value>[^{}]+)\}\s*\$|\\boxed\{(?<value>[^{}]+)\}")]
    private static partial Regex BoxedCaptureRegex();

    [GeneratedRegex(@"\$\s*\\boxed\{[^{}]+\}\s*\$|\\boxed\{[^{}]+\}")]
    private static partial Regex BoxedExpressionRegex();
}
