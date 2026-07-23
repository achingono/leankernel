namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Discriminates the source of a document ingestion job.
/// </summary>
public enum DocumentIngestionSource
{
    /// <summary>
    /// Document originated from a channel attachment.
    /// </summary>
    ChannelAttachment,

    /// <summary>
    /// Document originated from a configured watch folder.
    /// </summary>
    WatchedFile,

    /// <summary>
    /// Document was uploaded directly via the upload API.
    /// </summary>
    Upload,
}
