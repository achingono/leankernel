namespace LeanKernel.Core.Models;

/// <summary>
/// Canonical message envelope used across all channels.
/// </summary>
public sealed class LeanKernelMessage
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or sets the channel id.
    /// </summary>
    public required string ChannelId { get; init; }
    /// <summary>
    /// Gets or sets the sender id.
    /// </summary>
    public required string SenderId { get; init; }
    /// <summary>
    /// Gets or sets the content.
    /// </summary>
    public required string Content { get; init; }
    /// <summary>
    /// Gets or sets the timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the reply to id.
    /// </summary>
    public string? ReplyToId { get; init; }
    /// <summary>
    /// Gets or sets the metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}
