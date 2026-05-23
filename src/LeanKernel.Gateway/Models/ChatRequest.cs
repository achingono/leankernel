namespace LeanKernel.Gateway;

public sealed record ChatRequest
{
    public string? Message { get; init; }
    public string? UserId { get; init; }
    public string? ChannelId { get; init; }
    public string? SessionId { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record ChatResponse
{
    public required string Response { get; init; }
    public string? SessionId { get; init; }
}
