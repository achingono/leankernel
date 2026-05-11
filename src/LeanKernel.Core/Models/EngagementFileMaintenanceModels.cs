namespace LeanKernel.Core.Models;

/// <summary>
/// Request to perform deterministic maintenance on engagement files.
/// </summary>
/// <param name="UserMessage">The user message that triggered maintenance.</param>
/// <param name="SourceDocumentNames">Document names that should be resolved and read before updating files.</param>
/// <param name="TargetFiles">Engagement file names to update. When empty, all engagement files are considered.</param>
public sealed record EngagementFileMaintenanceRequest(
    string UserMessage,
    IReadOnlyList<string> SourceDocumentNames,
    IReadOnlyList<string> TargetFiles);

/// <summary>
/// Result of deterministic engagement file maintenance.
/// </summary>
public sealed record EngagementFileMaintenanceResult
{
    /// <summary>
    /// Gets whether maintenance completed without errors.
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// Gets the source files found for requested document names.
    /// </summary>
    public IReadOnlyList<string> SourceFilesFound { get; init; } = [];

    /// <summary>
    /// Gets the source files that were successfully read or text-extracted.
    /// </summary>
    public IReadOnlyList<string> SourceFilesRead { get; init; } = [];

    /// <summary>
    /// Gets engagement files whose content changed.
    /// </summary>
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];

    /// <summary>
    /// Gets engagement files verified after maintenance.
    /// </summary>
    public IReadOnlyList<string> VerifiedFiles { get; init; } = [];

    /// <summary>
    /// Gets target files that were skipped and the reason.
    /// </summary>
    public IReadOnlyList<string> SkippedFiles { get; init; } = [];

    /// <summary>
    /// Gets source-backed excerpts written or considered during maintenance.
    /// </summary>
    public IReadOnlyList<string> SourceExcerpts { get; init; } = [];

    /// <summary>
    /// Gets errors encountered during maintenance.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets whether any engagement file changed.
    /// </summary>
    public bool HasChanges => ChangedFiles.Count > 0;
}
