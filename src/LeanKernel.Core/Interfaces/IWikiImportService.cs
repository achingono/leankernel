namespace LeanKernel.Core.Interfaces;

using LeanKernel.Core.Configuration;

/// <summary>
/// Runs one-shot wiki import flows from external systems into LeanKernel wiki storage.
/// </summary>
public interface IWikiImportService
{
    /// <summary>
    /// Imports OpenClaw wiki facts and dimensions into the canonical LeanKernel wiki format.
    /// </summary>
    Task<OpenClawImportResult> ImportOpenClawAsync(OpenClawImportRequest request, CancellationToken ct);
}

/// <summary>
/// Import request options for an OpenClaw wiki import run.
/// </summary>
public sealed record OpenClawImportRequest(
    bool DryRun,
    bool SkipRemoteSync,
    WikiExtractionStrategy Strategy = WikiExtractionStrategy.Deterministic,
    LLMExtractionConfig? LLMConfig = null);

/// <summary>
/// Result payload for OpenClaw wiki import runs.
/// </summary>
public sealed record OpenClawImportResult(
    string RunId,
    bool DryRun,
    WikiExtractionStrategy Strategy,
    int PagesProcessed,
    int FactsExtracted,
    int FactsImported,
    int Quarantined,
    int EntriesUpserted,
    string AuditPath);

/// <summary>
/// Configuration for LLM-based fact extraction.
/// </summary>
public sealed record LLMExtractionConfig(
    string? OllamaBaseUrl = null,
    string? Model = null,
    double? Temperature = null,
    int? TimeoutSeconds = null);
