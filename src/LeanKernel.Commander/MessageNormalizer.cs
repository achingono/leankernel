using LeanKernel.Core.Models;

namespace LeanKernel.Commander;

/// <summary>
/// Normalizes platform-specific message formats into LeanKernelMessage.
/// </summary>
public static class MessageNormalizer
{
    /// <summary>
    /// Represents the normalize.
    /// </summary>
    public static LeanKernelMessage Normalize(
        string channelId,
        string senderId,
        string rawContent,
        string? replyToId = null,
        Dictionary<string, string>? metadata = null)
    {
        return new LeanKernelMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            ChannelId = channelId,
            SenderId = senderId,
            Content = rawContent.Trim(),
            Timestamp = DateTimeOffset.UtcNow,
            ReplyToId = replyToId,
            Metadata = metadata ?? []
        };
    }
}
