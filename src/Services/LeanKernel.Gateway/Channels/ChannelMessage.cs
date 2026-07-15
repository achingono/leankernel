namespace LeanKernel.Gateway.Channels;

/// <summary>
/// Represents a message sent or received through a channel.
/// </summary>
public sealed record ChannelMessage
{
    /// <summary>
    /// Gets the channel identifier that carries this message.
    /// </summary>
    public required string ChannelId { get; init; }

    /// <summary>
    /// Gets the sender's identifier within the channel.
    /// </summary>
    public required string SenderId { get; init; }

    /// <summary>
    /// Gets the recipient identifier (for outbound messages).
    /// </summary>
    public string? RecipientId { get; init; }

    /// <summary>
    /// Gets the message text content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets the timestamp of the message.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the optional list of attachments.
    /// </summary>
    public IReadOnlyList<Attachment>? Attachments { get; init; }
}

/// <summary>
/// Represents a file attachment carried by a channel message.
/// </summary>
public sealed record Attachment
{
    /// <summary>
    /// Gets the file name.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the MIME content type.
    /// </summary>
    public string ContentType { get; init; } = "application/octet-stream";

    /// <summary>
    /// Gets the binary data.
    /// </summary>
    public required byte[] Data { get; init; }
}
