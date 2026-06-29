namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration settings for context assembly.
/// </summary>
public sealed class ContextConfig
{
    /// <summary>
    /// Gets or sets the budget ratio for the system prompt.
    /// </summary>
    public double SystemPromptBudgetRatio { get; set; } = 0.15;

    /// <summary>
    /// Gets or sets the budget ratio for wiki facts.
    /// </summary>
    public double WikiFactsBudgetRatio { get; set; } = 0.20;

    /// <summary>
    /// Gets or sets the budget ratio for the conversation.
    /// </summary>
    public double ConversationBudgetRatio { get; set; } = 0.40;

    /// <summary>
    /// Gets or sets the budget ratio for retrieval results.
    /// </summary>
    public double RetrievalBudgetRatio { get; set; } = 0.20;

    /// <summary>
    /// Gets or sets the budget ratio for tool outputs.
    /// </summary>
    public double ToolsBudgetRatio { get; set; } = 0.05;

    /// <summary>
    /// Gets or sets the budget ratio for the response headroom.
    /// </summary>
    public double ResponseHeadroomRatio { get; set; } = 0.25;

    /// <summary>
    /// Gets or sets the number of recent turns to keep verbatim.
    /// </summary>
    public int RecentTurnsVerbatim { get; set; } = 6;

    /// <summary>
    /// Gets or sets the maximum number of compacted turns.
    /// </summary>
    public int CompactedTurnsMax { get; set; } = 10;

    /// <summary>
    /// Gets or sets the entity subject boost for entity extraction.
    /// </summary>
    public double EntitySubjectBoost { get; set; } = 1.5;

    /// <summary>
    /// Gets or sets the supporting entity threshold.
    /// </summary>
    public double SupportingEntityThreshold { get; set; } = 0.4;

    /// <summary>
    /// Gets or sets the entity expansion depth.
    /// </summary>
    public int EntityExpansionDepth { get; set; } = 2;
}
