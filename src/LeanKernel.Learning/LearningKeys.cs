namespace LeanKernel.Learning;

internal static class LearningKeys
{
    public const string CapabilityGapsPageKey = "learning/capability-gaps";
    public const string EngagementMetricsPageKey = "learning/engagement-metrics";

    public static string CreateFactPageKey(string sessionId, string turnId, int index)
        => $"learning/facts/{SanitizeSegment(sessionId)}/{SanitizeSegment(turnId)}/{index:D2}";

    private static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var buffer = new char[value.Length];
        var length = 0;

        foreach (var character in value.Trim())
        {
            buffer[length++] = char.IsLetterOrDigit(character) || character is '-' or '_'
                ? char.ToLowerInvariant(character)
                : '-';
        }

        return new string(buffer, 0, length).Trim('-');
    }
}
