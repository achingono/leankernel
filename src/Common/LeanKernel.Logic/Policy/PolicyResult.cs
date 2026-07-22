namespace LeanKernel.Logic.Policy;

/// <summary>
/// Represents the result of a single policy evaluation.
/// Domain policies return <see cref="Allow"/> or <see cref="Deny"/>
/// with an optional reason string for logging and audit.
/// </summary>
public sealed record PolicyResult
{
    /// <summary>
    /// Gets a value indicating whether the action is allowed by this policy.
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Gets the optional reason describing why the action was denied.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Returns an allowed result.
    /// </summary>
    /// <returns>A policy result with <see cref="IsAllowed"/> set to <c>true</c>.</returns>
    public static PolicyResult Allow() => new() { IsAllowed = true };

    /// <summary>
    /// Returns a denied result with the specified reason.
    /// </summary>
    /// <param name="reason">The reason for denial.</param>
    /// <returns>A policy result with <see cref="IsAllowed"/> set to <c>false</c>.</returns>
    public static PolicyResult Deny(string reason) => new() { IsAllowed = false, Reason = reason };
}