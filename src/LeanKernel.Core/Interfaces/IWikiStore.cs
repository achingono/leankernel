using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Persistence layer for the 5W1H wiki filesystem.
/// </summary>
public interface IWikiStore
{
    /// <summary>
    /// Gets a wiki entry by identifier.
    /// </summary>
    Task<WikiEntry?> GetAsync(string entryId, CancellationToken ct);
    /// <summary>
    /// Queries wiki entries using dimension and text filters.
    /// </summary>
    Task<IReadOnlyList<WikiEntry>> QueryAsync(WikiQuery query, CancellationToken ct);
    /// <summary>
    /// Creates or updates a wiki entry.
    /// </summary>
    Task UpsertAsync(WikiEntry entry, CancellationToken ct);
    /// <summary>
    /// Deletes a wiki entry by identifier.
    /// </summary>
    Task DeleteAsync(string entryId, CancellationToken ct);
    /// <summary>
    /// Lists wiki entries for a single 5W1H dimension.
    /// </summary>
    Task<IReadOnlyList<WikiEntry>> ListByDimensionAsync(Enums.WikiDimension dimension, CancellationToken ct);

    /// <summary>Ingest extracted facts from an LLM response into the wiki.</summary>
    Task IngestFactsAsync(IEnumerable<WikiEntry> entries, CancellationToken ct);
}
