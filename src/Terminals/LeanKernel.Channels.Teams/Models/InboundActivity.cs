namespace LeanKernel.Channels.Teams;

public sealed record InboundActivity(
    string ActivityId,
    string SenderId,
    string ConversationId,
    string ServiceUrl,
    string Text,
    string BearerToken,
    IReadOnlyList<string> AttachmentUrls);
