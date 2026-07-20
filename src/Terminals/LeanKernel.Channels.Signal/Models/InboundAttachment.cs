namespace LeanKernel.Channels.Signal;

public sealed record InboundAttachment(string AttachmentId, string ContentType, string FileName, string ImageDataUrl)
{
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
