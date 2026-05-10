namespace LeanKernel.Core.Models;

/// <summary>
/// The fully assembled context window ready for the LLM.
/// Built by the Archivist's gatekeeper; consumed by the Thinker.
/// </summary>
public sealed record ConversationContext
{
    /// <summary>
    /// Gets or sets the system prompt.
    /// </summary>
    public required string SystemPrompt { get; init; }
    /// <summary>
    /// Gets or sets the history.
    /// </summary>
    public required List<ConversationTurn> History { get; init; }
    /// <summary>
    /// Gets or sets the wiki lean kernels.
    /// </summary>
    public required List<RelevanceScore> WikiLeanKernels { get; init; }
    /// <summary>
    /// Gets or sets the retrieved lean kernels.
    /// </summary>
    public required List<RelevanceScore> RetrievedLeanKernels { get; init; }
    /// <summary>
    /// Gets or sets the active tool names.
    /// </summary>
    public required List<string> ActiveToolNames { get; init; }

    /// <summary>
    /// Gets or sets the estimated total tokens.
    /// </summary>
    public int EstimatedTotalTokens { get; init; }
    /// <summary>
    /// Gets or sets the exclusion log.
    /// </summary>
    public List<string> ExclusionLog { get; init; } = [];

    /// <summary>
    /// Optional onboarding instruction injected on the first message of a session
    /// when SELF.md or USER.md have gaps. Appended to the system prompt.
    /// </summary>
    public string? OnboardingInstruction { get; init; }
}

/// <summary>
/// Represents the conversation turn.
/// </summary>
public sealed record ConversationTurn
{
    /// <summary>
    /// Gets or sets the role.
    /// </summary>
    public required string Role { get; init; }
    /// <summary>
    /// Gets or sets the content.
    /// </summary>
    public required string Content { get; init; }
    /// <summary>
    /// Gets or sets the timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
    /// <summary>
    /// Gets or sets the is compacted.
    /// </summary>
    public bool IsCompacted { get; init; }
}
