namespace LeanKernel.Abstractions.Configuration;

public sealed class RoutingConfig
{
    public bool Enabled { get; set; } = false;
    public int QualityMinOutputLength { get; set; } = 50;
    public double QualityMinConstraintCoverage { get; set; } = 0.6;
    public int MaxEscalationAttempts { get; set; } = 2;
    public List<string> RefusalPatterns { get; set; } =
    [
        "I cannot",
        "I'm sorry, I can't",
        "As an AI language model",
        "I'm not able to",
        "I don't have the ability"
    ];
    public bool ShadowRoutingEnabled { get; set; } = false;
    public string ShadowModel { get; set; } = string.Empty;
    public ModelTierConfig Economy { get; set; } = new() { Model = "gpt-4o-mini", MaxTokens = 4096 };
    public ModelTierConfig Standard { get; set; } = new() { Model = "gpt-4o", MaxTokens = 8192 };
    public ModelTierConfig Premium { get; set; } = new() { Model = "claude-sonnet-4-20250514", MaxTokens = 16384 };
    public ComplexityScoringConfig Scoring { get; set; } = new();
}

public sealed class ModelTierConfig
{
    public string Model { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 4096;
    public double CostWeight { get; set; } = 1.0;
}

public sealed class ComplexityScoringConfig
{
    public int HighComplexityTokenThreshold { get; set; } = 2000;
    public int MediumComplexityTokenThreshold { get; set; } = 500;
    public double ToolUsageComplexityBoost { get; set; } = 0.3;
    public double MultiTurnComplexityBoost { get; set; } = 0.2;
    public double LongContextComplexityBoost { get; set; } = 0.2;
}
