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
}
