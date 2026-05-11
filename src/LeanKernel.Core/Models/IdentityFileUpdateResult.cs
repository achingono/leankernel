namespace LeanKernel.Core.Models;

/// <summary>
/// Result of an identity/engagement file update attempt.
/// </summary>
public sealed record IdentityFileUpdateResult
{
    /// <summary>Whether the update pipeline completed without an unhandled failure.</summary>
    public bool Success { get; init; } = true;

    /// <summary>Identity files whose contents changed during the update.</summary>
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];

    /// <summary>Identity files that exist after the update attempt.</summary>
    public IReadOnlyList<string> VerifiedFiles { get; init; } = [];

    /// <summary>Failures encountered while attempting the update.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Whether any identity file content changed.</summary>
    public bool HasChanges => ChangedFiles.Count > 0;

    public static IdentityFileUpdateResult Failed(params string[] errors) => new()
    {
        Success = false,
        Errors = errors
    };
}
