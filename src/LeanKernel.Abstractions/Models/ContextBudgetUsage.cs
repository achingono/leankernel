namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents the usage of context budget by different components.
/// </summary>
public sealed record ContextBudgetUsage
{
    /// <summary>
    /// Gets the number of tokens used by the system prompt.
    /// </summary>
    public int SystemPromptUsed { get; init; }

    /// <summary>
    /// Gets the number of tokens used by wiki facts.
    /// </summary>
    public int WikiFactsUsed { get; init; }

    /// <summary>
    /// Gets the number of tokens used by the conversation.
    /// </summary>
    public int ConversationUsed { get; init; }

    /// <summary>
    /// Gets the number of tokens used by retrieval.
    /// </summary>
    public int RetrievalUsed { get; init; }

    /// <summary>
    /// Gets the number of tokens used by tools.
    /// </summary>
    public int ToolsUsed { get; init; }

    /// <summary>
    /// Gets the total number of tokens used.
    /// </summary>
    public int TotalUsed => SystemPromptUsed + WikiFactsUsed + ConversationUsed + RetrievalUsed + ToolsUsed;
}
