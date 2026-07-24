namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Represents the result of an enrichment job execution.
/// </summary>
/// <param name="IngestionJobId">The parent ingestion job identifier.</param>
/// <param name="EnrichmentJobId">The enrichment job identifier.</param>
/// <param name="DreamRunId">The optional dream run identifier.</param>
/// <param name="Success">A value indicating whether the enrichment succeeded.</param>
public sealed record EnrichmentResult(
    Guid IngestionJobId,
    Guid EnrichmentJobId,
    Guid? DreamRunId,
    bool Success);