namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration for document ingestion background processing.
/// </summary>
public sealed class DocumentIngestionConfig
{
    /// <summary>
    /// Gets or sets whether document ingestion background processing is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of concurrent document ingestion jobs.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum number of documents that can be queued.
    /// </summary>
    public int MaxQueuedDocuments { get; set; } = 100;

    /// <summary>
    /// Gets or sets whether the document import folder should be monitored.
    /// </summary>
    public bool WatchFolderEnabled { get; set; }

    /// <summary>
    /// Gets or sets the folder to monitor for dropped documents.
    /// </summary>
    public string WatchFolderPath { get; set; } = "/app/data/documents";

    /// <summary>
    /// Gets or sets the file glob used by the folder watcher.
    /// </summary>
    public string WatchFilter { get; set; } = "*.*";

    /// <summary>
    /// Gets or sets whether subdirectories are monitored.
    /// </summary>
    public bool WatchIncludeSubdirectories { get; set; } = true;

    /// <summary>
    /// Gets or sets whether files already present at startup should be imported.
    /// </summary>
    public bool WatchStartupScanEnabled { get; set; }

    /// <summary>
    /// Gets or sets how long to wait for a dropped file to settle before queueing it.
    /// </summary>
    public int WatchSettleDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Gets or sets the fallback polling interval in seconds. Set to 0 to disable polling.
    /// </summary>
    public int WatchPollingIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets tags added to documents imported from the watched folder.
    /// </summary>
    public List<string> WatchDefaultTags { get; set; } = ["auto-import"];

    /// <summary>
    /// Gets or sets the service-owned storage path for uploaded document copies.
    /// </summary>
    public string ManagedStoragePath { get; set; } = "/app/data/managed-documents";

    /// <summary>
    /// Gets or sets the timeout in seconds for enqueueing a document.
    /// When the queue is full, the producer waits up to this duration.
    /// </summary>
    public int WatchEnqueueTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for enqueueing a watched file.
    /// </summary>
    public int WatchMaxRetries { get; set; } = 5;

    /// <summary>
    /// Gets or sets the base delay in seconds for exponential backoff on retry.
    /// </summary>
    public int WatchRetryBaseDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Gets or sets the maximum delay in seconds for exponential backoff on retry.
    /// </summary>
    public int WatchRetryMaxDelaySeconds { get; set; } = 300;
}
