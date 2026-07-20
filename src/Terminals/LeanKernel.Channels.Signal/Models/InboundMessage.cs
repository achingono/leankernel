namespace LeanKernel.Channels.Signal;

public sealed record InboundMessage(
    string Account,
    string Sender,
    string Text,
    string BearerToken,
    IReadOnlyList<InboundAttachment> Attachments);
