namespace LeanKernel.Services.Learning;

/// <summary>
/// Configuration options for the learning pipeline service.
/// </summary>
public sealed class LearningSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the learning pipeline is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the capacity of the turn event queue.
    /// </summary>
    public int TurnQueueCapacity { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of concurrent learning pipeline executions.
    /// </summary>
    public int MaxConcurrency { get; set; } = 1;
}