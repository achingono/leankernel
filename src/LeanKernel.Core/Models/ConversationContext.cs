namespace LeanKernel.Core.Models;

/// <summary>
/// The fully assembled context window ready for the LLM.
/// Built by the Archivist's gatekeeper; consumed by the Thinker.
/// </summary>
public sealed record ConversationContext
{
    public required string SystemPrompt { get; init; }
    public required List<ConversationTurn> History { get; init; }
    public required List<RelevanceScore> WikiLeanKernels { get; init; }
    public required List<RelevanceScore> RetrievedLeanKernels { get; init; }
    public required List<string> ActiveToolNames { get; init; }

    public int EstimatedTotalTokens { get; init; }
    public List<string> ExclusionLog { get; init; } = [];
}

public sealed record ConversationTurn
{
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public bool IsCompacted { get; init; }
}
