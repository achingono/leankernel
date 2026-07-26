namespace LeanKernel.Channels.Signal;

using LeanKernel.Entities;

/// <summary>
/// Represents an attachment received in an inbound Signal message.
/// </summary>
/// <param name="AttachmentId">The Signal attachment identifier.</param>
/// <param name="ContentType">The MIME content type of the attachment.</param>
/// <param name="FileName">The original file name of the attachment.</param>
/// <param name="ImageDataUrl">A data URL containing the image bytes, if the attachment is an image that has been downloaded.</param>
/// <param name="FileDataUrl">A data URL containing the file bytes, if the attachment is a non-image file that has been downloaded.</param>
public sealed record InboundAttachment(
    string AttachmentId,
    string ContentType,
    string FileName,
    string ImageDataUrl,
    string FileDataUrl)
    : ChannelAttachmentEnvelope(AttachmentId, ContentType, FileName, ImageDataUrl, FileDataUrl);