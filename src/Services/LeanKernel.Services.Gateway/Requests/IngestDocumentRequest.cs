namespace LeanKernel.Services.Gateway.Requests;

/// <summary>
/// Request body for base64 document ingestion.
/// </summary>
public sealed class IngestDocumentRequest
{
    /// <summary>
    /// The original file name.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// The base64-encoded file data (optionally as a data URL).
    /// </summary>
    public string FileData { get; set; } = string.Empty;

    /// <summary>
    /// The MIME content type of the file.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Optional channel identifier. Defaults to the authenticated channel.
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Optional availability scope (user, channel, or tenant). Defaults to user.
    /// </summary>
    public string? AvailabilityScope { get; set; }
}