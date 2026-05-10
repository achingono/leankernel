namespace LeanKernel.Core.Models;

/// <summary>
/// Result of attempting to deliver a message through a channel.
/// </summary>
public sealed record ChannelDeliveryResult
{
    /// <summary>
    /// Gets whether message delivery succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the channel that attempted delivery.
    /// </summary>
    public required string Channel { get; init; }

    /// <summary>
    /// Gets when delivery was attempted or completed.
    /// </summary>
    public DateTime DeliveredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the delivery error, when delivery failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets whether the failure is retryable.
    /// </summary>
    public bool IsRetryable { get; init; }

    /// <summary>
    /// Gets the suggested retry delay for retryable failures.
    /// </summary>
    public TimeSpan? SuggestedRetryDelay { get; init; }

    /// <summary>
    /// Gets a channel-specific delivery reference, when available.
    /// </summary>
    public string? DeliveryReference { get; init; }

    /// <summary>
    /// Creates a successful delivery result.
    /// </summary>
    /// <param name="channel">The channel that delivered the message.</param>
    /// <param name="reference">The channel-specific delivery reference.</param>
    /// <returns>A successful delivery result.</returns>
    public static ChannelDeliveryResult Successful(string channel, string? reference = null) =>
        new()
        {
            Success = true,
            Channel = channel,
            DeliveryReference = reference
        };

    /// <summary>
    /// Creates a failed delivery result.
    /// </summary>
    /// <param name="channel">The channel that attempted delivery.</param>
    /// <param name="error">The delivery error.</param>
    /// <param name="retryable">Whether the failure can be retried.</param>
    /// <param name="retryDelay">The suggested retry delay.</param>
    /// <returns>A failed delivery result.</returns>
    public static ChannelDeliveryResult Failed(
        string channel,
        string error,
        bool retryable = false,
        TimeSpan? retryDelay = null) =>
        new()
        {
            Success = false,
            Channel = channel,
            Error = error,
            IsRetryable = retryable,
            SuggestedRetryDelay = retryDelay
        };
}
