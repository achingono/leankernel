namespace LeanKernel.Thinker.Enhancement;

/// <summary>
/// Shared category definitions for engagement intent classification and downstream formatting.
/// </summary>
public static class EngagementIntentCategories
{
    public const string Communication = "communication";
    public const string Autonomy = "autonomy";
    public const string EngagementModel = "engagement_model";
    public const string Identity = "identity";
    public const string TimeBoundary = "time_boundary";
    public const string Tools = "tools";
    public const string Priorities = "priorities";
    public const string Correction = "correction";
    public const string None = "none";

    public static IReadOnlyList<string> All { get; } =
    [
        Communication,
        Autonomy,
        EngagementModel,
        Identity,
        TimeBoundary,
        Tools,
        Priorities,
        Correction,
        None
    ];

    public static string NormalizeOrNone(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return None;

        var normalized = category.Trim().ToLowerInvariant();
        return All.Contains(normalized, StringComparer.Ordinal) ? normalized : None;
    }

    public static string ToIdentityUpdateLabel(string category)
    {
        return NormalizeOrNone(category) switch
        {
            Communication => "Communications",
            Autonomy => "Autonomy",
            EngagementModel => "Engagement model",
            Identity => "Agent name",
            TimeBoundary => "Availability",
            Tools => "Tools and integrations",
            Priorities => "Top Priorities",
            Correction => "Correction",
            _ => "Engagement model"
        };
    }
}