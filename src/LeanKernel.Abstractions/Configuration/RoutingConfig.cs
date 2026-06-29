namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration settings for routing and quality assessment.
/// </summary>
public sealed class RoutingConfig
{
    /// <summary>
    /// Gets or sets whether routing is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the minimum output length for quality assessment.
    /// </summary>
    public int QualityMinOutputLength { get; set; } = 50;

    /// <summary>
    /// Gets or sets the minimum constraint coverage required for quality assessment.
    /// </summary>
    public double QualityMinConstraintCoverage { get; set; } = 0.6;

    /// <summary>
    /// Gets or sets the maximum number of escalation attempts.
    /// </summary>
    public int MaxEscalationAttempts { get; set; } = 2;

    /// <summary>
    /// Gets or sets the list of refusal patterns to look for in responses.
    /// </summary>
    public List<string> RefusalPatterns { get; set; } =
    [
        "I cannot",
        "I'm sorry, I can't",
        "As an AI language model",
        "I'm not able to",
        "I don't have the ability"
    ];

    /// <summary>
    /// Gets or sets whether shadow routing is enabled.
    /// </summary>
    public bool ShadowRoutingEnabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the model used for shadow routing.
    /// </summary>
    public string ShadowModel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the economy model tier configuration.
    /// </summary>
    public ModelTierConfig Economy { get; set; } = new() { Model = "small", MaxTokens = 4096 };

    /// <summary>
    /// Gets or sets the standard model tier configuration.
    /// </summary>
    public ModelTierConfig Standard { get; set; } = new() { Model = "medium", MaxTokens = 8192 };

    /// <summary>
    /// Gets or sets the premium model tier configuration.
    /// </summary>
    public ModelTierConfig Premium { get; set; } = new() { Model = "large", MaxTokens = 16384 };

    /// <summary>
    /// Gets or sets the complexity scoring configuration.
    /// </summary>
    public ComplexityScoringConfig Scoring { get; set; } = new();
}

/// <summary>
/// Configuration settings for a model tier.
/// </summary>
public sealed class ModelTierConfig
{
    /// <summary>
    /// Gets or sets the model name.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of tokens.
    /// </summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Gets or sets the cost weight for the model tier.
    /// </summary>
    public double CostWeight { get; set; } = 1.0;
}

/// <summary>
/// Configuration for scoring task complexity.
/// </summary>
public sealed class ComplexityScoringConfig
{
    /// <summary>
    /// Gets or sets the token threshold for high complexity.
    /// </summary>
    public int HighComplexityTokenThreshold { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the token threshold for medium complexity.
    /// </summary>
    public int MediumComplexityTokenThreshold { get; set; } = 500;

    /// <summary>
    /// Gets or sets the complexity boost for tool usage.
    /// </summary>
    public double ToolUsageComplexityBoost { get; set; } = 0.3;

    /// <summary>
    /// Gets or sets the complexity boost for multi-turn interactions.
    /// </summary>
    public double MultiTurnComplexityBoost { get; set; } = 0.2;

    /// <summary>
    /// Gets or sets the complexity boost for long context.
    /// </summary>
    public double LongContextComplexityBoost { get; set; } = 0.2;
}
