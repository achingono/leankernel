using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface IChannel
{
    string ChannelId { get; }
    bool IsConnected { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task SendAsync(string recipientId, string message, CancellationToken ct = default);
    event Func<ChannelMessage, Task>? MessageReceived;
}

public sealed record ChannelMessage
{
    public required string ChannelId { get; init; }
    public required string SenderId { get; init; }
    public string? RecipientId { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<Attachment>? Attachments { get; init; }
}
