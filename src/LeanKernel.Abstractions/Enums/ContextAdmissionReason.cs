namespace LeanKernel.Abstractions.Enums;

/// <summary>
/// Represents context admission reason values.
/// </summary>
public enum ContextAdmissionReason
{
    HighRelevanceScore,
    EntityMatch,
    RecentHistory,
    SystemPrompt,
    ToolMetadata,
    ExplicitInclusion
}
