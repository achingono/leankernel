namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configures the small-model memory reasoning pipeline.
/// </summary>
public sealed class MemorySettings
{
    /// <summary>
    /// Gets or sets the model identifier used for memory reasoning.
    /// </summary>
    public string ModelId { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Gets or sets the maximum number of output tokens returned by the reasoning model.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 512;

    /// <summary>
    /// Gets or sets the maximum number of concurrent reasoning calls.
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Gets or sets the timeout, in seconds, for each reasoning request.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Gets or sets a value indicating whether memory reasoning is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}