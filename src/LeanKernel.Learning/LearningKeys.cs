namespace LeanKernel.Learning;

/// <summary>
/// Defines canonical knowledge page keys used by the learning subsystem.
/// </summary>
internal static class LearningKeys
{
    /// <summary>
    /// Knowledge page key that stores aggregated capability gap records.
    /// </summary>
    public const string CapabilityGapsPageKey = "learning/capability-gaps";

    /// <summary>
    /// Knowledge page key that stores aggregated engagement metrics.
    /// </summary>
    public const string EngagementMetricsPageKey = "learning/engagement-metrics";

    /// <summary>
    /// Creates a deterministic key for an extracted fact page.
    /// </summary>
    /// <param name="sessionId">The session identifier associated with the turn.</param>
    /// <param name="turnId">The turn identifier within the session.</param>
    /// <param name="index">The 1-based fact index for the turn.</param>
    /// <returns>A normalized key path suitable for knowledge storage.</returns>
    public static string CreateFactPageKey(string sessionId, string turnId, int index)
        => $"learning/facts/{SanitizeSegment(sessionId)}/{SanitizeSegment(turnId)}/{index:D2}";

    /// <summary>
    /// Normalizes a path segment to lowercase alphanumeric characters with dashes for unsupported characters.
    /// </summary>
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
