namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a structured agent response.
/// </summary>
public sealed record AgentResponse
{
    /// <summary>
    /// Gets the response text content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets optional attachments to deliver with the response.
    /// </summary>
    public IReadOnlyList<Attachment>? Attachments { get; init; }

    /// <summary>
    /// Gets optional execution metadata captured for the completed turn.
    /// </summary>
    public TurnExecutionInfo? Execution { get; init; }
}
