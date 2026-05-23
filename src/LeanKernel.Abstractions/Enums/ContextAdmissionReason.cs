namespace LeanKernel.Abstractions.Enums;

public enum ContextAdmissionReason
{
    HighRelevanceScore,
    EntityMatch,
    RecentHistory,
    SystemPrompt,
    ToolMetadata,
    ExplicitInclusion
}
