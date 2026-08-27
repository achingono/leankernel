namespace LeanKernel.Channels.Signal;

/// <summary>
/// Represents an inbound Signal message with its associated metadata and credentials.
/// </summary>
/// <param name="Account">The Signal account number that received the message.</param>
/// <param name="Sender">The sender's Signal number.</param>
/// <param name="Text">The message text content.</param>
/// <param name="BearerToken">The bearer token associated with the sender for gateway authentication.</param>
/// <param name="Attachments">The attachments included in the message.</param>
public sealed record InboundMessage(
    string Account,
    string Sender,
    string Text,
    string BearerToken,
    IReadOnlyList<InboundAttachment> Attachments)
{
    /// <summary>
    /// Gets the stable gateway conversation identifier for this Signal peer.
    /// The receiving account is included so the same sender can converse independently
    /// with separate provisioned Signal accounts.
    /// </summary>
    public string ConversationId => $"signal:{this.Account}:{this.Sender}";
}
