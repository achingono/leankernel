using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Filesystem-backed 5W1H wiki store. Each entry is a JSON file
/// organized by dimension: data/wiki/{who,what,where,when,why,how}/.
/// </summary>
public sealed class WikiStore : IWikiStore
{
    private readonly string _basePath;
    private readonly ILogger<WikiStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public WikiStore(IOptions<LeanKernelConfig> config, ILogger<WikiStore> logger)
    {
        _basePath = config.Value.Wiki.BasePath;
        _logger = logger;
        EnsureDirectories();
    }

    public async Task<WikiEntry?> GetAsync(string entryId, CancellationToken ct)
    {
        var path = ResolvePath(entryId);
        if (!File.Exists(path)) return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<WikiEntry>(stream, JsonOptions, ct);
    }

    public async Task<IReadOnlyList<WikiEntry>> QueryAsync(WikiQuery query, CancellationToken ct)
    {
        var results = new List<WikiEntry>();

        var dimensions = query.Dimensions.Count > 0
            ? query.Dimensions
            : Enum.GetValues<WikiDimension>().ToHashSet();

        foreach (var dim in dimensions)
        {
            var entries = await ListByDimensionAsync(dim, ct);
            foreach (var entry in entries)
            {
                if (query.MinConfidence > 0 && entry.Facts.All(f => f.Confidence < query.MinConfidence))
                    continue;

                if (query.MaxAge.HasValue && entry.LastAccessed < DateTimeOffset.UtcNow - query.MaxAge.Value)
                    continue;

                if (!string.IsNullOrEmpty(query.TextQuery) &&
                    !MatchesText(entry, query.TextQuery))
                    continue;

                results.Add(entry);
                if (results.Count >= query.MaxResults) return results;
            }
        }

        return results;
    }

    public async Task UpsertAsync(WikiEntry entry, CancellationToken ct)
    {
        var path = ResolvePath(entry.Id);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, ct);
        _logger.LogDebug("Wiki entry upserted: {EntryId}", entry.Id);
    }

    public Task DeleteAsync(string entryId, CancellationToken ct)
    {
        var path = ResolvePath(entryId);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogDebug("Wiki entry deleted: {EntryId}", entryId);
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<WikiEntry>> ListByDimensionAsync(WikiDimension dimension, CancellationToken ct)
    {
        var dimDir = Path.Combine(_basePath, dimension.ToString().ToLowerInvariant());
        if (!Directory.Exists(dimDir)) return [];

        var entries = new List<WikiEntry>();
        foreach (var file in Directory.GetFiles(dimDir, "*.json"))
        {
            try
            {
                await using var stream = File.OpenRead(file);
                var entry = await JsonSerializer.DeserializeAsync<WikiEntry>(stream, JsonOptions, ct);
                if (entry is not null) entries.Add(entry);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize wiki entry: {File}", file);
            }
        }

        return entries;
    }

    public async Task IngestFactsAsync(IEnumerable<WikiEntry> entries, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            var existing = await GetAsync(entry.Id, ct);
            if (existing is not null)
            {
                // Merge facts: update existing claims, add new ones
                var merged = MergeFacts(existing, entry);
                await UpsertAsync(merged, ct);
            }
            else
            {
                await UpsertAsync(entry, ct);
            }
        }
    }

    private static WikiEntry MergeFacts(WikiEntry existing, WikiEntry incoming)
    {
        var mergedFacts = new List<WikiFact>(existing.Facts);

        foreach (var newFact in incoming.Facts)
        {
            var match = mergedFacts.FindIndex(f =>
                f.Claim.Equals(newFact.Claim, StringComparison.OrdinalIgnoreCase));

            if (match >= 0)
            {
                // Update confidence and confirmation time
                mergedFacts[match] = newFact with
                {
                    Confidence = Math.Max(mergedFacts[match].Confidence, newFact.Confidence),
                    LastConfirmed = DateTimeOffset.UtcNow
                };
            }
            else
            {
                mergedFacts.Add(newFact);
            }
        }

        return existing with
        {
            Facts = mergedFacts,
            LastAccessed = DateTimeOffset.UtcNow,
            Relations = existing.Relations.Union(incoming.Relations).Distinct().ToList()
        };
    }

    private string ResolvePath(string entryId)
    {
        // entryId format: "who-alice-smith" → wiki/who/alice-smith.json
        var parts = entryId.Split('-', 2);
        var dimension = parts.Length > 0 ? parts[0] : "what";
        var name = parts.Length > 1 ? parts[1] : entryId;
        return Path.Combine(_basePath, dimension, $"{name}.json");
    }

    private static bool MatchesText(WikiEntry entry, string query)
    {
        var lower = query.ToLowerInvariant();
        return entry.Subject.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
               entry.Facts.Any(f => f.Claim.Contains(lower, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureDirectories()
    {
        foreach (var dim in Enum.GetValues<WikiDimension>())
        {
            Directory.CreateDirectory(Path.Combine(_basePath, dim.ToString().ToLowerInvariant()));
        }
        Directory.CreateDirectory(Path.Combine(_basePath, ".LeanKernel"));
    }
}
