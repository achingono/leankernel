namespace LeanKernel.Thinker.Enhancement;

/// <summary>
/// Result of classifying whether a user turn should update engagement identity files.
/// </summary>
public sealed record EngagementIntentClassification(
    bool ShouldUpdate,
    string Category,
    string NormalizedInsight,
    string Reason)
{
    /// <summary>
    /// Represents a non-update classification.
    /// </summary>
    public static EngagementIntentClassification NoUpdate(string reason = "No engagement update intent detected.") =>
        new(false, EngagementIntentCategories.None, "", reason);
}
