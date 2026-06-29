namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a message within the LeanKernel ecosystem.
/// </summary>
public sealed record LeanKernelMessage
{
    /// <summary>
    /// Gets the text content of the message.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets the identifier of the sender.
    /// </summary>
    public required string SenderId { get; init; }

    /// <summary>
    /// Gets the identifier of the channel where the message originated.
    /// </summary>
    public required string ChannelId { get; init; }

    /// <summary>
    /// Gets the timestamp when the message was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the session identifier associated with the message, if any.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the metadata associated with the message.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Gets the list of attachments in the message.
    /// </summary>
    public IReadOnlyList<Attachment>? Attachments { get; init; }
}

/// <summary>
/// Represents an attachment within a LeanKernel message.
/// </summary>
public sealed record Attachment
{
    /// <summary>
    /// Gets the filename of the attachment.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the MIME type of the attachment content.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Gets the byte array of the attachment data.
    /// </summary>
    public required byte[] Data { get; init; }
}
