namespace LeanKernel.Channels.Teams;

/// <summary>Represents an inbound Teams activity with resolved routing information.</summary>
/// <param name="ActivityId">The activity identifier from Teams.</param>
/// <param name="SenderId">The sender identifier.</param>
/// <param name="ConversationId">The conversation identifier.</param>
/// <param name="ServiceUrl">The service URL for the Bot Framework connector.</param>
/// <param name="Text">The message text.</param>
/// <param name="BearerToken">The bearer token for gateway authentication.</param>
/// <param name="AttachmentUrls">The list of attachment URLs included in the message.</param>
public sealed record InboundActivity(
    string ActivityId,
    string SenderId,
    string ConversationId,
    string ServiceUrl,
    string Text,
    string BearerToken,
    IReadOnlyList<string> AttachmentUrls);