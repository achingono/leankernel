namespace LeanKernel.Core.Models;

/// <summary>
/// Token budget allocation for context window assembly.
/// The gatekeeper fills each slice competitively; unused budget
/// is not redistributed — it stays as headroom for the response.
/// </summary>
public sealed class ContextBudget
{
    public required int TotalTokens { get; init; }

    /// <summary>System prompt + persona. ~15% of budget.</summary>
    public int SystemPromptBudget => (int)(TotalTokens * 0.15);

    /// <summary>5W1H structured facts. ~20% of budget.</summary>
    public int WikiFactsBudget => (int)(TotalTokens * 0.20);

    /// <summary>Conversation history (sliding window). ~40% of budget.</summary>
    public int ConversationBudget => (int)(TotalTokens * 0.40);

    /// <summary>RAG LeanKernels from vector search. ~20% of budget.</summary>
    public int RetrievalBudget => (int)(TotalTokens * 0.20);

    /// <summary>Active tool definitions. ~5% of budget.</summary>
    public int ToolsBudget => (int)(TotalTokens * 0.05);

    /// <summary>Create a budget from a model's context window, reserving 25% for the response.</summary>
    public static ContextBudget FromModelWindow(int contextWindowTokens)
        => new() { TotalTokens = (int)(contextWindowTokens * 0.75) };
}
