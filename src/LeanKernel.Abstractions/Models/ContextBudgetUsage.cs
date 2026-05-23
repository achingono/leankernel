namespace LeanKernel.Abstractions.Models;

public sealed record ContextBudgetUsage
{
    public int SystemPromptUsed { get; init; }
    public int WikiFactsUsed { get; init; }
    public int ConversationUsed { get; init; }
    public int RetrievalUsed { get; init; }
    public int ToolsUsed { get; init; }
    public int TotalUsed => SystemPromptUsed + WikiFactsUsed + ConversationUsed + RetrievalUsed + ToolsUsed;
}
