namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configuration settings for the enrichment worker.
/// </summary>
public sealed class EnrichmentSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether enrichment is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent enrichment jobs.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum queue capacity for enrichment jobs.
    /// </summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>
    /// Gets or sets the lease timeout in minutes for enrichment jobs.
    /// </summary>
    public int LeaseTimeoutMinutes { get; set; } = 5;
}