namespace LeanKernel.Channels.Signal;

/// <summary>
/// Represents an attachment received in an inbound Signal message.
/// </summary>
/// <param name="AttachmentId">The Signal attachment identifier.</param>
/// <param name="ContentType">The MIME content type of the attachment.</param>
/// <param name="FileName">The original file name of the attachment.</param>
/// <param name="ImageDataUrl">A data URL containing the image bytes, if the attachment is an image that has been downloaded.</param>
public sealed record InboundAttachment(string AttachmentId, string ContentType, string FileName, string ImageDataUrl)
{
    /// <summary>
    /// Gets whether this attachment is an image based on the content type.
    /// </summary>
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
