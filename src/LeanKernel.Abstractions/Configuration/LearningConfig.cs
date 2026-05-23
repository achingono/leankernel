namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configures asynchronous post-turn learning behavior.
/// </summary>
public sealed class LearningConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether post-turn learning is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether fact extraction is enabled.
    /// </summary>
    public bool FactExtractionEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether capability-gap detection is enabled.
    /// </summary>
    public bool CapabilityGapDetectionEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether engagement tracking is enabled.
    /// </summary>
    public bool EngagementTrackingEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of concurrent learning tasks.
    /// </summary>
    public int MaxConcurrentLearningTasks { get; set; } = 2;

    /// <summary>
    /// Gets or sets the bounded queue capacity for turn events.
    /// </summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>
    /// Gets or sets the model used for fact extraction.
    /// </summary>
    public string ExtractionModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Gets or sets the temperature used for fact extraction.
    /// </summary>
    public double ExtractionTemperature { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets the minimum combined turn length required before fact extraction runs.
    /// </summary>
    public int MinTurnLengthForExtraction { get; set; } = 50;
}
