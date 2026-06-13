namespace LeanKernel.Gateway;

/// <summary>
/// Request DTO for the /api/chat endpoint.
/// When forwarded-auth is enabled, <c>UserId</c> is ignored in favour of the
/// authenticated user identity derived from OIDC claims or forwarded headers.
/// </summary>
public sealed record ChatRequest
{
    /// <summary>The user message to process.</summary>
    public string? Message { get; init; }
    /// <summary>
    /// Optional caller-supplied user identifier.
    /// Ignored when forwarded-auth is enabled; the authenticated identity is used instead.
    /// </summary>
    public string? UserId { get; init; }
    /// <summary>Optional channel identifier. Defaults to <c>"api"</c>.</summary>
    public string? ChannelId { get; init; }
    /// <summary>Optional session identifier. Auto-created if omitted.</summary>
    public string? SessionId { get; init; }
    /// <summary>Optional metadata passed through to the turn pipeline.</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record ChatResponse
{
    public required string Response { get; init; }
    public string? SessionId { get; init; }
}
