namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Runs one-shot migration of legacy wiki entries from data/wiki/llm into canonical dimensions.
/// </summary>
public interface IWikiMigrationService
{
    /// <summary>
    /// Execute the migration and return a summary payload.
    /// </summary>
    Task<WikiMigrationResult> MigrateAsync(CancellationToken ct);
}

/// <summary>
/// Result payload for wiki migration runs.
/// </summary>
public sealed record WikiMigrationResult(
    int Migrated,
    int Quarantined,
    int Skipped,
    string SentinelPath);

