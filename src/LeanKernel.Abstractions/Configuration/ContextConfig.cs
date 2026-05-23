namespace LeanKernel.Abstractions.Configuration;

public sealed class ContextConfig
{
    public double SystemPromptBudgetRatio { get; set; } = 0.15;
    public double WikiFactsBudgetRatio { get; set; } = 0.20;
    public double ConversationBudgetRatio { get; set; } = 0.40;
    public double RetrievalBudgetRatio { get; set; } = 0.20;
    public double ToolsBudgetRatio { get; set; } = 0.05;
    public double ResponseHeadroomRatio { get; set; } = 0.25;
    public int RecentTurnsVerbatim { get; set; } = 6;
    public int CompactedTurnsMax { get; set; } = 10;
    public double EntitySubjectBoost { get; set; } = 1.5;
    public double SupportingEntityThreshold { get; set; } = 0.4;
    public int EntityExpansionDepth { get; set; } = 2;
}
