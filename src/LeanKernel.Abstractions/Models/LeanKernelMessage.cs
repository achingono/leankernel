namespace LeanKernel.Abstractions.Models;

public sealed record LeanKernelMessage
{
    public required string Content { get; init; }
    public required string SenderId { get; init; }
    public required string ChannelId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? SessionId { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public IReadOnlyList<Attachment>? Attachments { get; init; }
}

public sealed record Attachment
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required byte[] Data { get; init; }
}
