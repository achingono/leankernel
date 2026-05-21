using System.Text.RegularExpressions;

namespace LeanKernel.Core.Models;

/// <summary>
/// Classifies statements as durable identity facts or transient task instructions.
/// </summary>
public static partial class IdentityDurabilityHeuristics
{
    /// <summary>
    /// Returns <see langword="true"/> when a value appears to be a durable identity or preference fact.
    /// </summary>
    public static bool IsDurableFact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 15 || trimmed.Length > 180)
        {
            return false;
        }

        if (QuestionSuffixRegex().IsMatch(trimmed) || QuestionStartRegex().IsMatch(trimmed))
        {
            return false;
        }

        if (ImperativeStartRegex().IsMatch(trimmed))
        {
            return false;
        }

        if (RelativeTemporalRegex().IsMatch(trimmed) ||
            DateTimeReferenceRegex().IsMatch(trimmed))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns <see langword="true"/> when a line appears transient or task-oriented.
    /// </summary>
    public static bool IsTransientInstruction(string value) => !IsDurableFact(value);

    [GeneratedRegex(@"\?\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex QuestionSuffixRegex();

    [GeneratedRegex(@"^\s*(?:-\s*)?(?:what|when|where|who|why|how|can|could|should|would|is|are|do|does|did)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex QuestionStartRegex();

    [GeneratedRegex(@"^\s*(?:-\s*)?(?:please\s+)?(?:help(?:\s+me)?|can\s+you|could\s+you|schedule|remind(?:\s+me)?|set(?:\s+up)?|create|draft|prepare|find|identify|choose|review|check|send|write|plan|book)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ImperativeStartRegex();

    [GeneratedRegex(@"\b(?:today|tomorrow|tonight|soon|later|upcoming)\b|\b(?:this|next|coming)\s+(?:week|weekend|month|quarter|year|morning|afternoon|evening|monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b|\b(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex RelativeTemporalRegex();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}(?::\d{2})?(?:Z|[+-]\d{2}:\d{2})?)?\b|\b\d{1,2}/\d{1,2}(?:/\d{2,4})?\b|\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\s+\d{1,2}(?:,?\s+\d{4})?\b|\b(?:at|by)\s+\d{1,2}(?::\d{2})?\s*(?:am|pm)?\b|\bin\s+\d+\s+(?:day|week|month|year)s?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex DateTimeReferenceRegex();
}
