using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// One-shot migrator for legacy data/wiki/llm records.
/// </summary>
public sealed class WikiMigrationService : IWikiMigrationService
{
    private readonly IWikiStore _wikiStore;
    private readonly string _wikiBasePath;
    private readonly string _metaPath;
    private readonly string _legacyPath;
    private readonly string _quarantinePath;
    private readonly string _ledgerPath;
    private readonly string _sentinelPath;
    private readonly ILogger<WikiMigrationService> _logger;

    public WikiMigrationService(
        IWikiStore wikiStore,
        IOptions<LeanKernelConfig> config,
        ILogger<WikiMigrationService> logger)
    {
        _wikiStore = wikiStore;
        _wikiBasePath = config.Value.Wiki.BasePath;
        var metaFolder = config.Value.Wiki.MetaFolder;
        _metaPath = Path.Combine(_wikiBasePath, metaFolder);
        _legacyPath = Path.Combine(_wikiBasePath, "llm");
        _quarantinePath = Path.Combine(_metaPath, "quarantine");
        _ledgerPath = Path.Combine(_metaPath, "migration.json");
        _sentinelPath = Path.Combine(_metaPath, "migration.completed");
        _logger = logger;
    }

    public async Task<WikiMigrationResult> MigrateAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_metaPath);

        if (File.Exists(_sentinelPath))
        {
            return new WikiMigrationResult(0, 0, 0, _sentinelPath);
        }

        if (!Directory.Exists(_legacyPath))
        {
            await File.WriteAllTextAsync(_sentinelPath, DateTimeOffset.UtcNow.ToString("O"), ct);
            return new WikiMigrationResult(0, 0, 0, _sentinelPath);
        }

        var legacyFiles = Directory
            .EnumerateFiles(_legacyPath, "*.md", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var preexistingSubjects = await BuildCanonicalSubjectMapAsync(ct);
        var runRecords = new List<MigrationRecord>();
        var migrated = 0;
        var quarantined = 0;
        var skipped = 0;

        foreach (var sourceFile in legacyFiles)
        {
            ct.ThrowIfCancellationRequested();

            var relativeSource = Path.GetRelativePath(_wikiBasePath, sourceFile).Replace('\\', '/');
            var fallbackId = $"llm-{Path.GetFileNameWithoutExtension(sourceFile)}";
            var parsed = WikiStore.ParseMarkdown(await File.ReadAllTextAsync(sourceFile, ct), fallbackId);
            if (parsed is null || parsed.Facts.Count == 0)
            {
                quarantined += await QuarantineAsync(sourceFile, relativeSource, "parse-or-empty", runRecords, ct);
                continue;
            }

            var inferredDimension = parsed.Dimension;
            var canonicalSubject = string.IsNullOrWhiteSpace(parsed.Subject)
                ? fallbackId
                : parsed.Subject.Trim();
            var normalizedSubject = WikiFactMapper.Slugify(canonicalSubject);
            if (string.IsNullOrWhiteSpace(normalizedSubject))
            {
                quarantined += await QuarantineAsync(sourceFile, relativeSource, "empty-subject", runRecords, ct);
                continue;
            }

            if (preexistingSubjects.TryGetValue(normalizedSubject, out var existingDimension) &&
                existingDimension != inferredDimension)
            {
                quarantined += await QuarantineAsync(sourceFile, relativeSource, "cross-dimension-collision", runRecords, ct);
                continue;
            }

            preexistingSubjects[normalizedSubject] = inferredDimension;
            var canonicalId = $"{inferredDimension.ToString().ToLowerInvariant()}-{normalizedSubject}";
            var canonicalEntry = parsed with
            {
                Id = canonicalId,
                Dimension = inferredDimension,
                Subject = canonicalSubject,
                Aliases = parsed.Aliases
                    .Append(Path.GetFileNameWithoutExtension(sourceFile))
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            var existing = await _wikiStore.GetAsync(canonicalId, ct);
            var merged = existing is null ? canonicalEntry : MergeEntries(existing, canonicalEntry);
            await _wikiStore.UpsertAsync(merged, ct);
            File.Delete(sourceFile);

            migrated++;
            runRecords.Add(new MigrationRecord
            {
                Source = relativeSource,
                TargetEntryId = canonicalId,
                Reason = "migrated",
                MigratedFactKeys = merged.Facts
                    .Select(f => f.NormalizedKey ?? WikiFactMapper.NormalizeClaim(f.Claim))
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }

        if (!Directory.EnumerateFileSystemEntries(_legacyPath).Any())
        {
            Directory.Delete(_legacyPath, recursive: true);
        }

        await AppendLedgerAsync(runRecords, ct);
        await File.WriteAllTextAsync(_sentinelPath, DateTimeOffset.UtcNow.ToString("O"), ct);
        _logger.LogInformation("Wiki migration complete: {Migrated} migrated, {Quarantined} quarantined, {Skipped} skipped", migrated, quarantined, skipped);
        return new WikiMigrationResult(migrated, quarantined, skipped, _sentinelPath);
    }

    private async Task<Dictionary<string, WikiDimension>> BuildCanonicalSubjectMapAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, WikiDimension>(StringComparer.OrdinalIgnoreCase);
        foreach (var dimension in Enum.GetValues<WikiDimension>())
        {
            var entries = await _wikiStore.ListByDimensionAsync(dimension, ct);
            foreach (var entry in entries)
            {
                var subject = WikiFactMapper.Slugify(entry.Subject);
                if (!string.IsNullOrWhiteSpace(subject))
                {
                    map[subject] = dimension;
                }
            }
        }

        return map;
    }

    private async Task<int> QuarantineAsync(
        string sourceFile,
        string relativeSource,
        string reason,
        List<MigrationRecord> runRecords,
        CancellationToken ct)
    {
        var quarantineTarget = Path.Combine(_quarantinePath, relativeSource);
        Directory.CreateDirectory(Path.GetDirectoryName(quarantineTarget)!);
        if (File.Exists(quarantineTarget))
        {
            File.Delete(quarantineTarget);
        }

        File.Move(sourceFile, quarantineTarget);
        runRecords.Add(new MigrationRecord
        {
            Source = relativeSource,
            Reason = reason,
            TargetEntryId = null
        });
        await Task.CompletedTask;
        return 1;
    }

    private async Task AppendLedgerAsync(List<MigrationRecord> runRecords, CancellationToken ct)
    {
        var existing = new List<MigrationRecord>();
        if (File.Exists(_ledgerPath))
        {
            try
            {
                existing = JsonSerializer.Deserialize<List<MigrationRecord>>(
                    await File.ReadAllTextAsync(_ledgerPath, ct),
                    WikiStoreJsonOptions()) ?? [];
            }
            catch
            {
                existing = [];
            }
        }

        existing.AddRange(runRecords.Select(r => r with { Timestamp = DateTimeOffset.UtcNow }));
        await File.WriteAllTextAsync(
            _ledgerPath,
            JsonSerializer.Serialize(existing, WikiStoreJsonOptions()),
            ct);
    }

    private static JsonSerializerOptions WikiStoreJsonOptions()
        => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

    private static WikiEntry MergeEntries(WikiEntry existing, WikiEntry incoming)
    {
        var mergedFacts = existing.Facts.ToList();
        foreach (var fact in incoming.Facts)
        {
            var incomingKey = fact.NormalizedKey ?? WikiFactMapper.NormalizeClaim(fact.Claim);
            var existingIndex = mergedFacts.FindIndex(existingFact =>
                string.Equals(
                    existingFact.NormalizedKey ?? WikiFactMapper.NormalizeClaim(existingFact.Claim),
                    incomingKey,
                    StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                var current = mergedFacts[existingIndex];
                mergedFacts[existingIndex] = fact with
                {
                    Confidence = Math.Max(current.Confidence, fact.Confidence),
                    LastConfirmed = current.LastConfirmed > fact.LastConfirmed ? current.LastConfirmed : fact.LastConfirmed,
                    Tags = current.Tags.Union(fact.Tags, StringComparer.OrdinalIgnoreCase).ToList()
                };
            }
            else
            {
                mergedFacts.Add(fact);
            }
        }

        return existing with
        {
            Facts = mergedFacts,
            Summary = string.IsNullOrWhiteSpace(existing.Summary) ? incoming.Summary : existing.Summary,
            Aliases = existing.Aliases.Union(incoming.Aliases, StringComparer.OrdinalIgnoreCase).ToList(),
            Tags = existing.Tags.Union(incoming.Tags, StringComparer.OrdinalIgnoreCase).ToList(),
            Relations = existing.Relations.Union(incoming.Relations).ToList()
        };
    }

    private sealed record MigrationRecord
    {
        public string Source { get; init; } = string.Empty;
        public string? TargetEntryId { get; init; }
        public string Reason { get; init; } = string.Empty;
        public List<string>? MigratedFactKeys { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }
}
