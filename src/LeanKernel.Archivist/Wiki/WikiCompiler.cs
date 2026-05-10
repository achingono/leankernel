using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Compiles and maintains the wiki: deduplicates facts, prunes stale entries,
/// updates confidence scores, and rebuilds the index digest.
/// </summary>
public sealed class WikiCompiler
{
    private readonly IWikiStore _wiki;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<WikiCompiler> _logger;

    /// <summary>
    /// Represents the wiki compiler.
    /// </summary>
    public WikiCompiler(
        IWikiStore wiki,
        IOptions<LeanKernelConfig> config,
        ILogger<WikiCompiler> logger)
    {
        _wiki = wiki;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// Run a full maintenance pass: prune stale facts, deduplicate, update stats.
    /// </summary>
    public async Task CompileAsync(CancellationToken ct)
    {
        _logger.LogInformation("Wiki compilation starting...");

        var totalPruned = 0;
        var totalDeduped = 0;

        foreach (var dimension in Enum.GetValues<Core.Enums.WikiDimension>())
        {
            var entries = await _wiki.ListByDimensionAsync(dimension, ct);

            foreach (var entry in entries)
            {
                var (pruned, deduped, updated) = ProcessEntry(entry);
                totalPruned += pruned;
                totalDeduped += deduped;

                if (updated is not null)
                {
                    await _wiki.UpsertAsync(updated, ct);
                }
                else if (pruned > 0 && entry.Facts.Count == 0)
                {
                    await _wiki.DeleteAsync(entry.Id, ct);
                }
            }
        }

        _logger.LogInformation(
            "Wiki compilation complete: {Pruned} facts pruned, {Deduped} duplicates removed",
            totalPruned, totalDeduped);
    }

    internal (int Pruned, int Deduped, WikiEntry? Updated) ProcessEntry(WikiEntry entry)
    {
        var facts = new List<WikiFact>(entry.Facts);
        var originalCount = facts.Count;

        // Prune stale facts (low confidence + old)
        var staleThreshold = DateTimeOffset.UtcNow.AddDays(-_config.Wiki.StaleFactDays);
        var pruned = facts.RemoveAll(f =>
            f.Confidence < _config.Wiki.MinConfidenceThreshold &&
            f.LastConfirmed < staleThreshold);

        // Deduplicate (case-insensitive claim matching)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = facts.RemoveAll(f => !seen.Add(f.Claim));

        // Cap at max facts per entry
        if (facts.Count > _config.Wiki.MaxFactsPerEntry)
        {
            facts = facts
                .OrderByDescending(f => f.Confidence)
                .ThenByDescending(f => f.LastConfirmed)
                .Take(_config.Wiki.MaxFactsPerEntry)
                .ToList();
            pruned += originalCount - facts.Count - pruned - deduped;
        }

        if (pruned == 0 && deduped == 0)
            return (0, 0, null);

        var updated = entry with { Facts = facts };
        return (pruned, deduped, updated);
    }
}
