using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Persistence layer for the 5W1H wiki filesystem.
/// </summary>
public interface IWikiStore
{
    Task<WikiEntry?> GetAsync(string entryId, CancellationToken ct);
    Task<IReadOnlyList<WikiEntry>> QueryAsync(WikiQuery query, CancellationToken ct);
    Task UpsertAsync(WikiEntry entry, CancellationToken ct);
    Task DeleteAsync(string entryId, CancellationToken ct);
    Task<IReadOnlyList<WikiEntry>> ListByDimensionAsync(Enums.WikiDimension dimension, CancellationToken ct);

    /// <summary>Ingest extracted facts from an LLM response into the wiki.</summary>
    Task IngestFactsAsync(IEnumerable<WikiEntry> entries, CancellationToken ct);
}
