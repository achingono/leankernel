namespace LeanKernel.Entities;

/// <summary>
/// Normalized attachment envelope shared across channel transports and gateway ingestion.
/// </summary>
/// <param name="AttachmentId">The channel-native attachment identifier.</param>
/// <param name="ContentType">The MIME content type.</param>
/// <param name="FileName">The original file name, when provided.</param>
/// <param name="ImageDataUrl">Inline image payload represented as a data URL.</param>
/// <param name="FileDataUrl">Inline non-image payload represented as a data URL.</param>
public record ChannelAttachmentEnvelope(
    string AttachmentId,
    string ContentType,
    string FileName,
    string ImageDataUrl,
    string FileDataUrl)
{
    /// <summary>
    /// Gets a value indicating whether the attachment is an image based on the content type.
    /// </summary>
    public bool IsImage => this.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether inline non-image bytes are present.
    /// </summary>
    public bool HasFileData => !string.IsNullOrWhiteSpace(this.FileDataUrl);

    /// <summary>
    /// Gets a value indicating whether inline image bytes are present.
    /// </summary>
    public bool HasImageData => !string.IsNullOrWhiteSpace(this.ImageDataUrl);
}