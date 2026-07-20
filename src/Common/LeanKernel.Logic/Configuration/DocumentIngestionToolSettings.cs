namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Document ingestion tool configuration nested under <c>Agents:Tools:DocumentIngestion</c>.
/// </summary>
public sealed class DocumentIngestionToolSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether document ingestion is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of concurrent ingestion jobs.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 3;

    /// <summary>
    /// Gets or sets the ingestion queue capacity.
    /// </summary>
    public int QueueCapacity { get; set; } = 100;

    /// <summary>
    /// Gets or sets the enqueue timeout in seconds.
    /// </summary>
    public int EnqueueTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the folder watch settle delay in seconds.
    /// </summary>
    public int WatchSettleDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Gets or sets the maximum retry count for folder watch operations.
    /// </summary>
    public int WatchMaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay in seconds for retry backoff.
    /// </summary>
    public int WatchRetryBaseDelaySeconds { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum delay in seconds for retry backoff.
    /// </summary>
    public int WatchRetryMaxDelaySeconds { get; set; } = 60;
}