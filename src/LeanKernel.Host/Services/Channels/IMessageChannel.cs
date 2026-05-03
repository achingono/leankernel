namespace LeanKernel.Host.Services.Channels;

/// <summary>
/// Represents a communication channel adapter for delivering messages.
/// </summary>
public interface IMessageChannel
{
    /// <summary>
    /// Channel name (e.g., "Signal", "Discord", "Email").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Deliver a message to a recipient via this channel.
    /// </summary>
    /// <param name="recipient">Channel-specific recipient identifier (phone number, user ID, email, etc.)</param>
    /// <param name="content">Message content to send</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Delivery result with status and optional retry info</returns>
    Task<ChannelDeliveryResult> DeliverAsync(
        string recipient,
        string content,
        CancellationToken ct = default);

    /// <summary>
    /// Check if this channel is properly configured and ready to send.
    /// </summary>
    /// <returns>True if channel can send, false otherwise</returns>
    bool IsConfigured { get; }
}

/// <summary>
/// Result of attempting to deliver a message via a channel.
/// </summary>
public sealed record ChannelDeliveryResult
{
    /// <summary>
    /// True if message was delivered successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Channel name that attempted delivery.
    /// </summary>
    public required string Channel { get; init; }

    /// <summary>
    /// Delivery timestamp.
    /// </summary>
    public DateTime DeliveredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Error message if delivery failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// True if the error is transient and the message should be retried.
    /// </summary>
    public bool IsRetryable { get; init; }

    /// <summary>
    /// Suggested delay before retrying (if IsRetryable=true).
    /// Null means use default retry policy.
    /// </summary>
    public TimeSpan? SuggestedRetryDelay { get; init; }

    /// <summary>
    /// Channel-specific delivery ID or reference (for tracking purposes).
    /// </summary>
    public string? DeliveryReference { get; init; }

    public static ChannelDeliveryResult Successful(string channel, string? reference = null) =>
        new()
        {
            Success = true,
            Channel = channel,
            DeliveryReference = reference
        };

    public static ChannelDeliveryResult Failed(string channel, string error, bool retryable = false, TimeSpan? retryDelay = null) =>
        new()
        {
            Success = false,
            Channel = channel,
            Error = error,
            IsRetryable = retryable,
            SuggestedRetryDelay = retryDelay
        };
}
