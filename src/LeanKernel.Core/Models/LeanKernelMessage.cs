namespace LeanKernel.Core.Models;

/// <summary>
/// Canonical message envelope used across all channels.
/// </summary>
public sealed class LeanKernelMessage
{
    public required string Id { get; init; }
    public required string ChannelId { get; init; }
    public required string SenderId { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? ReplyToId { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
}
