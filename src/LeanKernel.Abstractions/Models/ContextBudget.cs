using LeanKernel.Abstractions.Configuration;

namespace LeanKernel.Abstractions.Models;

public sealed class ContextBudget
{
    public required int TotalTokens { get; init; }
    public required int SystemPromptBudget { get; init; }
    public required int WikiFactsBudget { get; init; }
    public required int ConversationBudget { get; init; }
    public required int RetrievalBudget { get; init; }
    public required int ToolsBudget { get; init; }

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
