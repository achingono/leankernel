namespace LeanKernel.Core.Models;

/// <summary>
/// Result of an engagement authorization check.
/// </summary>
public sealed class AuthorizationResult
{
    /// <summary>
    /// Gets whether the action is authorized.
    /// </summary>
    public required bool IsAuthorized { get; init; }

    /// <summary>
    /// Gets the optional explanation for the decision.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets the action type that was checked.
    /// </summary>
    public required string ActionType { get; init; }

    /// <summary>
    /// Gets when the decision was made.
    /// </summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
}
