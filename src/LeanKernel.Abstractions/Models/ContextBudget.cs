using LeanKernel.Abstractions.Configuration;

namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents the budget of tokens allocated for different context components.
/// </summary>
public sealed class ContextBudget
{
    /// <summary>
    /// Gets the total usable tokens.
    /// </summary>
    public required int TotalTokens { get; init; }

    /// <summary>
    /// Gets the budget for the system prompt.
    /// </summary>
    public required int SystemPromptBudget { get; init; }

    /// <summary>
    /// Gets the budget for wiki facts.
    /// </summary>
    public required int WikiFactsBudget { get; init; }

    /// <summary>
    /// Gets the budget for the conversation history.
    /// </summary>
    public required int ConversationBudget { get; init; }

    /// <summary>
    /// Gets the budget for retrieval results.
    /// </summary>
    public required int RetrievalBudget { get; init; }

    /// <summary>
    /// Gets the budget for tool outputs.
    /// </summary>
    public required int ToolsBudget { get; init; }

    /// <summary>
    /// Creates a budget based on the context window size and configuration.
    /// </summary>
    /// <param name="contextWindowTokens">The total context window size in tokens.</param>
    /// <param name="config">The context configuration.</param>
    /// <returns>A new <see cref="ContextBudget"/>.</returns>
    public static ContextBudget FromConfig(int contextWindowTokens, ContextConfig config)
    {
        var usable = (int)(contextWindowTokens * (1.0 - config.ResponseHeadroomRatio));
        return new ContextBudget
        {
            TotalTokens = usable,
            SystemPromptBudget = (int)(usable * config.SystemPromptBudgetRatio),
            WikiFactsBudget = (int)(usable * config.WikiFactsBudgetRatio),
            ConversationBudget = (int)(usable * config.ConversationBudgetRatio),
            RetrievalBudget = (int)(usable * config.RetrievalBudgetRatio),
            ToolsBudget = (int)(usable * config.ToolsBudgetRatio)
        };
    }
}
