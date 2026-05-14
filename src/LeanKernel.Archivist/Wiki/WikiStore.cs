using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Filesystem-backed wiki store with an on-disk metadata index.
/// </summary>
public sealed class WikiStore : IWikiStore
{
    private const int IndexVersion = 2;

    private static readonly Regex FactLineRegex = new(
        @"^-\s+(?<claim>.+?)(?:\s*<!--\{(?<meta>[^}]*)\}-->)?$",
        RegexOptions.Compiled);

    private static readonly Regex RelatedLinkRegex = new(
        @"^-\s+\[(?<text>[^\]]+)\]\((?<path>[^)]+)\)$",
        RegexOptions.Compiled);

    private readonly string _basePath;
    private readonly string _metaPath;
    private readonly string _indexPath;
    private readonly ILogger<WikiStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private WikiIndex _index = new()
    {
        Version = IndexVersion,
        FactPointers = BuildEmptyFactPointers()
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="WikiStore" /> class.
    /// </summary>
    public WikiStore(IOptions<LeanKernelConfig> config, ILogger<WikiStore> logger)
    {
        _basePath = config.Value.Wiki.BasePath;
        _metaPath = Path.Combine(_basePath, config.Value.Wiki.MetaFolder);
        _indexPath = Path.Combine(_metaPath, "index.json");
        _logger = logger;
        EnsureDirectories();
        _index = LoadOrRebuildIndex();
    }

    /// <inheritdoc />
    public async Task<WikiEntry?> GetAsync(string entryId, CancellationToken ct)
    {
        var indexEntry = _index.Entries.FirstOrDefault(e => e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase));
        if (indexEntry is null)
            return null;

        var path = Path.Combine(_basePath, indexEntry.FilePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            _index = RebuildIndexFromMarkdown();
            PersistIndex(_index);
            indexEntry = _index.Entries.FirstOrDefault(e => e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase));
            if (indexEntry is null)
                return null;

            path = Path.Combine(_basePath, indexEntry.FilePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                return null;
        }

        var content = await File.ReadAllTextAsync(path, ct);
        return ParseMarkdown(content, entryId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WikiEntry>> QueryAsync(WikiQuery query, CancellationToken ct)
    {
        var candidateEntryIds = GetCandidateEntryIds(query);
        var rankedEntryIds = RankCandidates(query, candidateEntryIds);

        var results = new List<WikiEntry>();
        foreach (var entryId in rankedEntryIds)
        {
            var entry = await GetAsync(entryId, ct);
            if (entry is null || !MatchesQuery(entry, query))
                continue;

            results.Add(entry);
            if (results.Count >= query.MaxResults)
                break;
        }

        return results;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(WikiEntry entry, CancellationToken ct)
    {
        ValidateEntryDimension(entry);
        await _writeLock.WaitAsync(ct);
        try
        {
            _index = ReloadIndexNoThrow();
            var path = ResolvePathForWrite(entry);
            var markdown = SerializeToMarkdown(entry);
            WriteAtomic(path, markdown);
            _index = UpsertIndexEntry(_index, entry);
            PersistIndex(_index);
            _logger.LogDebug("Wiki entry upserted: {EntryId}", entry.Id);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string entryId, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            _index = ReloadIndexNoThrow();
            var indexEntry = _index.Entries.FirstOrDefault(e => e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase));
            if (indexEntry is null)
                return;

            var path = Path.Combine(_basePath, indexEntry.FilePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogDebug("Wiki entry deleted: {EntryId}", entryId);
            }

            _index = RemoveIndexEntry(_index, entryId);
            PersistIndex(_index);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WikiEntry>> ListByDimensionAsync(WikiDimension dimension, CancellationToken ct)
    {
        var dim = dimension.ToString().ToLowerInvariant();
        var entryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_index.FactPointers.TryGetValue(dim, out var pointers))
        {
            foreach (var pointer in pointers)
                entryIds.Add(pointer.EntryId);
        }

        foreach (var entry in _index.Entries.Where(e => e.Dimension.Equals(dim, StringComparison.OrdinalIgnoreCase)))
            entryIds.Add(entry.Id);

        var results = new List<WikiEntry>();
        foreach (var entryId in entryIds)
        {
            var entry = await GetAsync(entryId, ct);
            if (entry is not null)
                results.Add(entry);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task IngestFactsAsync(IEnumerable<WikiEntry> entries, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            var existing = await GetAsync(entry.Id, ct);
            if (existing is not null)
            {
                var merged = MergeFacts(existing, entry);
                await UpsertAsync(merged, ct);
            }
            else
            {
                await UpsertAsync(entry, ct);
            }
        }
    }

    internal static string SerializeToMarkdown(WikiEntry entry)
    {
        var sb = new StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine($"id: {entry.Id}");
        sb.AppendLine($"dimension: {entry.Dimension.ToString().ToLowerInvariant()}");
        sb.AppendLine($"subject: {entry.Subject}");
        sb.AppendLine($"summary: {entry.Summary ?? string.Empty}");
        sb.AppendLine($"lastAccessed: {entry.LastAccessed:O}");
        sb.AppendLine($"accessCount: {entry.AccessCount}");
        sb.AppendLine("aliases:");
        foreach (var alias in entry.Aliases)
            sb.AppendLine($"  - {alias}");
        sb.AppendLine("tags:");
        foreach (var tag in entry.Tags)
            sb.AppendLine($"  - {tag}");
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine($"# {entry.Subject}");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(entry.Summary))
            sb.AppendLine(entry.Summary);
        sb.AppendLine();

        sb.AppendLine("## Facts");
        sb.AppendLine();
        foreach (var fact in entry.Facts)
            sb.AppendLine($"- {fact.Claim}");
        sb.AppendLine();

        sb.AppendLine("```yaml lk-facts");
        foreach (var fact in entry.Facts)
        {
            sb.AppendLine($"- claim: {ToYamlScalar(fact.Claim)}");
            sb.AppendLine($"  normalizedKey: {ToYamlScalar(fact.NormalizedKey ?? $"{entry.Id}|{WikiFactMapper.NormalizeClaim(fact.Claim)}")}");
            sb.AppendLine($"  sourceQuote: {ToYamlScalar(fact.SourceQuote)}");
            sb.AppendLine($"  source: {ToYamlScalar(fact.Source)}");
            sb.AppendLine($"  confidence: {fact.Confidence.ToString("F2", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"  lastConfirmed: {fact.LastConfirmed:yyyy-MM-dd}");
            sb.AppendLine("  context:");
            sb.AppendLine($"    who: {ToYamlScalar(fact.Context?.Who)}");
            sb.AppendLine($"    what: {ToYamlScalar(fact.Context?.What)}");
            sb.AppendLine($"    when: {ToYamlScalar(fact.Context?.When)}");
            sb.AppendLine($"    where: {ToYamlScalar(fact.Context?.Where)}");
            sb.AppendLine($"    why: {ToYamlScalar(fact.Context?.Why)}");
            sb.AppendLine($"    how: {ToYamlScalar(fact.Context?.How)}");
            sb.AppendLine("  tags:");
            foreach (var tag in fact.Tags)
                sb.AppendLine($"    - {ToYamlScalar(tag)}");
        }
        sb.AppendLine("```");

        sb.AppendLine();
        sb.AppendLine("## Also Known As");
        sb.AppendLine();
        foreach (var alias in entry.Aliases)
            sb.AppendLine($"- {alias}");

        if (entry.Relations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Related");
            sb.AppendLine();
            foreach (var relation in entry.Relations)
            {
                var (linkText, linkPath) = ResolveRelationLink(relation, entry.Dimension);
                sb.AppendLine($"- [{linkText}]({linkPath})");
            }
        }

        return sb.ToString();
    }

    internal static WikiEntry? ParseMarkdown(string content, string fallbackId)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (!TryReadFrontmatter(lines, out var endIdx, out var frontmatter))
            return null;

        var id = frontmatter.Scalars.GetValueOrDefault("id", fallbackId);
        var dimensionStr = frontmatter.Scalars.GetValueOrDefault("dimension", "what");
        var subject = frontmatter.Scalars.GetValueOrDefault("subject", "Unknown");
        var summary = frontmatter.Scalars.GetValueOrDefault("summary", string.Empty);
        var lastAccessed = ParseDateTimeOffset(frontmatter.Scalars.GetValueOrDefault("lastAccessed", ""));
        var accessCount = int.TryParse(frontmatter.Scalars.GetValueOrDefault("accessCount", "0"), out var ac) ? ac : 0;
        var aliases = frontmatter.Lists.GetValueOrDefault("aliases", []);
        var tags = frontmatter.Lists.GetValueOrDefault("tags", []);

        if (!Enum.TryParse<WikiDimension>(dimensionStr, ignoreCase: true, out var dimension))
            dimension = WikiDimension.What;

        var bodyLines = lines.Skip(endIdx + 1).ToList();
        var (facts, relations, bodyAliases, bodySummary) = ParseBodyLines(bodyLines, dimension);

        return new WikiEntry
        {
            Id = id,
            Dimension = dimension,
            Subject = subject,
            Summary = string.IsNullOrWhiteSpace(summary) ? bodySummary : summary,
            Facts = facts,
            Relations = relations,
            Aliases = aliases.Union(bodyAliases, StringComparer.OrdinalIgnoreCase).ToList(),
            Tags = tags.ToList(),
            LastAccessed = lastAccessed,
            AccessCount = accessCount
        };
    }

    private static bool TryReadFrontmatter(
        List<string> lines,
        out int endIdx,
        out ParsedFrontmatter frontmatter)
    {
        endIdx = -1;
        frontmatter = new ParsedFrontmatter();
        if (lines.Count < 3 || lines[0] != "---")
            return false;

        endIdx = lines.IndexOf("---", 1);
        if (endIdx < 0)
            return false;

        frontmatter = ParseFrontmatter(lines.Skip(1).Take(endIdx - 1));
        return true;
    }

    private static (List<WikiFact> Facts, List<string> Relations, List<string> Aliases, string? Summary) ParseBodyLines(
        List<string> bodyLines,
        WikiDimension dimension)
    {
        var facts = new List<WikiFact>();
        var relations = new List<string>();
        var aliases = new List<string>();
        var summaryBuilder = new StringBuilder();

        var canonicalFacts = ParseLkFactsBlock(bodyLines);
        if (canonicalFacts.Count > 0)
            facts.AddRange(canonicalFacts);

        var inSummarySection = false;
        var inAliasesSection = false;
        var inRelatedSection = false;
        var inFactsSection = false;
        var insideFactsCodeBlock = false;

        for (var i = 0; i < bodyLines.Count; i++)
        {
            var line = bodyLines[i];
            if (line.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                insideFactsCodeBlock = !insideFactsCodeBlock;
                continue;
            }
            if (insideFactsCodeBlock)
                continue;

            if (line.StartsWith("## Related", StringComparison.OrdinalIgnoreCase))
            {
                inRelatedSection = true;
                inAliasesSection = false;
                inSummarySection = false;
                inFactsSection = false;
                continue;
            }
            if (line.StartsWith("## Also Known As", StringComparison.OrdinalIgnoreCase))
            {
                inAliasesSection = true;
                inRelatedSection = false;
                inSummarySection = false;
                inFactsSection = false;
                continue;
            }
            if (line.StartsWith("## Summary", StringComparison.OrdinalIgnoreCase))
            {
                inSummarySection = true;
                inAliasesSection = false;
                inRelatedSection = false;
                inFactsSection = false;
                continue;
            }
            if (line.StartsWith("## Facts", StringComparison.OrdinalIgnoreCase))
            {
                inFactsSection = true;
                inSummarySection = false;
                inAliasesSection = false;
                inRelatedSection = false;
                continue;
            }

            if (line.StartsWith("## "))
            {
                inRelatedSection = false;
                inAliasesSection = false;
                inSummarySection = false;
                inFactsSection = false;
                continue;
            }

            if (inRelatedSection)
            {
                AddRelation(line, dimension, relations);
            }
            else if (inAliasesSection && line.TrimStart().StartsWith("- "))
            {
                aliases.Add(line.TrimStart()[2..].Trim());
            }
            else if (inSummarySection)
            {
                if (summaryBuilder.Length > 0 || !string.IsNullOrWhiteSpace(line))
                    summaryBuilder.AppendLine(line);
            }
            else if (inFactsSection && facts.Count == 0)
            {
                AddFact(line, facts);
            }
            else if (!inSummarySection && !inAliasesSection && !inRelatedSection && !inFactsSection && facts.Count == 0)
            {
                AddFact(line, facts);
            }
        }

        var summary = summaryBuilder.ToString().Trim();
        return (facts, relations, aliases, string.IsNullOrWhiteSpace(summary) ? null : summary);
    }

    private static void AddRelation(string line, WikiDimension dimension, List<string> relations)
    {
        var relMatch = RelatedLinkRegex.Match(line);
        if (!relMatch.Success)
            return;

        var relationId = ExtractRelationId(relMatch.Groups["path"].Value, dimension);
        if (!string.IsNullOrEmpty(relationId))
            relations.Add(relationId);
    }

    private static void AddFact(string line, List<WikiFact> facts)
    {
        var factMatch = FactLineRegex.Match(line);
        if (!factMatch.Success)
            return;

        var claim = factMatch.Groups["claim"].Value.Trim();
        var meta = factMatch.Groups["meta"].Value;
        facts.Add(ParseFact(claim, meta));
    }

    private static ParsedFrontmatter ParseFrontmatter(IEnumerable<string> lines)
    {
        var parsed = new ParsedFrontmatter();
        string? currentListKey = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("  - ") && !string.IsNullOrWhiteSpace(currentListKey))
            {
                if (!parsed.Lists.TryGetValue(currentListKey, out var list))
                {
                    list = [];
                    parsed.Lists[currentListKey] = list;
                }
                list.Add(line[4..].Trim());
                continue;
            }

            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0)
                continue;

            var key = line[..colonIdx].Trim();
            var value = line[(colonIdx + 1)..].Trim();
            if (string.IsNullOrEmpty(value))
            {
                currentListKey = key;
                if (!parsed.Lists.ContainsKey(key))
                    parsed.Lists[key] = [];
                continue;
            }

            currentListKey = null;
            parsed.Scalars[key] = value;
        }

        return parsed;
    }

    private static WikiFact ParseFact(string claim, string metaStr)
    {
        var fact = new WikiFact
        {
            Claim = claim,
            Confidence = 0.5,
            LastConfirmed = DateTimeOffset.UtcNow,
            EstimatedTokens = (int)Math.Ceiling(claim.Length / 4.0),
            NormalizedKey = null
        };

        if (string.IsNullOrWhiteSpace(metaStr))
            return fact;

        var pairs = metaStr.Split(',').Select(p => p.Trim());
        foreach (var pair in pairs)
        {
            var colonIdx = pair.IndexOf(':');
            if (colonIdx <= 0)
                continue;

            var key = pair[..colonIdx].Trim();
            var value = pair[(colonIdx + 1)..].Trim();

            switch (key.ToLowerInvariant())
            {
                case "confidence":
                    if (double.TryParse(value, CultureInfo.InvariantCulture, out var conf))
                        fact.Confidence = conf;
                    break;
                case "source":
                    fact = fact with { Source = value };
                    break;
                case "confirmed":
                    fact.LastConfirmed = ParseDateTimeOffset(value);
                    break;
                case "tokens":
                    if (int.TryParse(value, out var tokens))
                        fact.EstimatedTokens = tokens;
                    break;
            }
        }

        return fact;
    }

    private static string FormatFactMeta(WikiFact fact)
    {
        var parts = new List<string>
        {
            $"confidence: {fact.Confidence.ToString("F2", CultureInfo.InvariantCulture)}"
        };
        if (!string.IsNullOrEmpty(fact.Source))
            parts.Add($"source: {fact.Source}");
        parts.Add($"confirmed: {fact.LastConfirmed:yyyy-MM-dd}");
        if (fact.EstimatedTokens > 0)
            parts.Add($"tokens: {fact.EstimatedTokens}");
        return string.Join(", ", parts);
    }

    private static string ToYamlScalar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "''";

        return $"'{value.Replace("'", "''")}'";
    }

    private static List<WikiFact> ParseLkFactsBlock(List<string> bodyLines)
    {
        var facts = new List<WikiFact>();
        var start = bodyLines.FindIndex(line => line.Trim().Equals("```yaml lk-facts", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
            return facts;

        var end = bodyLines.FindIndex(start + 1, line => line.Trim().Equals("```", StringComparison.OrdinalIgnoreCase));
        if (end < 0)
            return facts;

        WikiFactBuilder? current = null;
        var inContext = false;
        var inTags = false;

        for (var i = start + 1; i < end; i++)
        {
            var line = bodyLines[i];
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (trimmed.StartsWith("- claim:"))
            {
                if (current is not null)
                    facts.Add(current.Build());
                current = new WikiFactBuilder
                {
                    Claim = UnquoteYaml(trimmed["- claim:".Length..].Trim())
                };
                inContext = false;
                inTags = false;
                continue;
            }

            if (current is null)
                continue;

            if (trimmed.Equals("context:", StringComparison.OrdinalIgnoreCase))
            {
                inContext = true;
                inTags = false;
                continue;
            }
            if (trimmed.Equals("tags:", StringComparison.OrdinalIgnoreCase))
            {
                inTags = true;
                inContext = false;
                continue;
            }

            if (inTags && trimmed.StartsWith("- "))
            {
                current.Tags.Add(UnquoteYaml(trimmed[2..].Trim()));
                continue;
            }

            if (inContext)
            {
                var colon = trimmed.IndexOf(':');
                if (colon <= 0)
                    continue;
                var key = trimmed[..colon].Trim().ToLowerInvariant();
                var value = UnquoteYaml(trimmed[(colon + 1)..].Trim());
                current.Context ??= new WikiFactContext();
                current.Context = key switch
                {
                    "who" => current.Context with { Who = value },
                    "what" => current.Context with { What = value },
                    "when" => current.Context with { When = value },
                    "where" => current.Context with { Where = value },
                    "why" => current.Context with { Why = value },
                    "how" => current.Context with { How = value },
                    _ => current.Context
                };
                continue;
            }

            var propertyColon = trimmed.IndexOf(':');
            if (propertyColon <= 0)
                continue;

            var property = trimmed[..propertyColon].Trim().ToLowerInvariant();
            var rawValue = trimmed[(propertyColon + 1)..].Trim();
            switch (property)
            {
                case "normalizedkey":
                    current.NormalizedKey = UnquoteYaml(rawValue);
                    break;
                case "sourcequote":
                    current.SourceQuote = UnquoteYaml(rawValue);
                    break;
                case "source":
                    current.Source = UnquoteYaml(rawValue);
                    break;
                case "confidence":
                    if (double.TryParse(rawValue, CultureInfo.InvariantCulture, out var confidence))
                        current.Confidence = confidence;
                    break;
                case "lastconfirmed":
                    current.LastConfirmed = ParseDateTimeOffset(rawValue);
                    break;
            }
        }

        if (current is not null)
            facts.Add(current.Build());

        return facts;
    }

    private static string? UnquoteYaml(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "''")
            return null;
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1].Replace("''", "'");
        return value;
    }

    private sealed class ParsedFrontmatter
    {
        public Dictionary<string, string> Scalars { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class WikiFactBuilder
    {
        public string Claim { get; init; } = "";
        public WikiFactContext? Context { get; set; }
        public string? SourceQuote { get; set; }
        public string? NormalizedKey { get; set; }
        public string? Source { get; set; }
        public double Confidence { get; set; } = 0.5;
        public DateTimeOffset LastConfirmed { get; set; } = DateTimeOffset.UtcNow;
        public List<string> Tags { get; } = [];

        public WikiFact Build()
        {
            return new WikiFact
            {
                Claim = Claim,
                Context = Context,
                SourceQuote = SourceQuote,
                NormalizedKey = NormalizedKey,
                Source = Source,
                Confidence = Confidence,
                LastConfirmed = LastConfirmed,
                EstimatedTokens = (int)Math.Ceiling(Claim.Length / 4.0),
                Tags = Tags
            };
        }
    }

    private static (string Text, string Path) ResolveRelationLink(string relationId, WikiDimension currentDimension)
    {
        var parts = relationId.Split('-', 2);
        var dim = parts.Length > 0 ? parts[0] : "what";
        var name = parts.Length > 1 ? parts[1] : relationId;
        var displayName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('-', ' '));
        var currentDim = currentDimension.ToString().ToLowerInvariant();
        var path = dim == currentDim ? $"./{name}.md" : $"../{dim}/{name}.md";
        return (displayName, path);
    }

    private static string ExtractRelationId(string linkPath, WikiDimension currentDimension)
    {
        var normalized = linkPath.Replace('\\', '/');
        var parts = normalized.Split('/');
        var fileName = Path.GetFileNameWithoutExtension(parts[^1]);

        if (parts.Length >= 2 && parts[^2] != ".")
            return $"{parts[^2]}-{fileName}";

        return $"{currentDimension.ToString().ToLowerInvariant()}-{fileName}";
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return dto;
        return DateTimeOffset.UtcNow;
    }

    private static WikiEntry MergeFacts(WikiEntry existing, WikiEntry incoming)
    {
        var mergedFacts = new List<WikiFact>(existing.Facts);
        foreach (var newFact in incoming.Facts)
        {
            var normalizedKey = ComputeMergeClaimKey(newFact);
            var matchIndex = mergedFacts.FindIndex(f =>
                string.Equals(
                    ComputeMergeClaimKey(f),
                    normalizedKey,
                    StringComparison.OrdinalIgnoreCase));

            if (matchIndex >= 0)
            {
                var old = mergedFacts[matchIndex];
                mergedFacts[matchIndex] = newFact with
                {
                    Confidence = Math.Max(old.Confidence, newFact.Confidence),
                    LastConfirmed = DateTimeOffset.UtcNow,
                    Tags = old.Tags.Union(newFact.Tags, StringComparer.OrdinalIgnoreCase).ToList()
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
            Summary = string.IsNullOrWhiteSpace(existing.Summary) ? incoming.Summary : existing.Summary,
            Aliases = existing.Aliases.Union(incoming.Aliases, StringComparer.OrdinalIgnoreCase).ToList(),
            Tags = existing.Tags.Union(incoming.Tags, StringComparer.OrdinalIgnoreCase).ToList(),
            LastAccessed = DateTimeOffset.UtcNow,
            Relations = existing.Relations.Union(incoming.Relations).Distinct().ToList()
        };
    }

    private static string ComputeMergeClaimKey(WikiFact fact)
    {
        var key = fact.NormalizedKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            var separator = key.IndexOf('|');
            if (separator >= 0 && separator < key.Length - 1)
                return key[(separator + 1)..];
            return key;
        }

        return WikiFactMapper.NormalizeClaim(fact.Claim);
    }

    private void ValidateEntryDimension(WikiEntry entry)
    {
        var expectedPrefix = $"{entry.Dimension.ToString().ToLowerInvariant()}-";
        if (!entry.Id.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Entry ID '{entry.Id}' conflicts with dimension '{entry.Dimension}'. Expected prefix '{expectedPrefix}'.");
        }
    }

    private string ResolvePathForWrite(WikiEntry entry)
    {
        var dim = entry.Dimension.ToString().ToLowerInvariant();
        var subjectSlug = entry.Id[(dim.Length + 1)..];
        return Path.Combine(_basePath, dim, $"{subjectSlug}.md");
    }

    private List<string> GetCandidateEntryIds(WikiQuery query)
    {
        if (query.Dimensions.Count == 0)
            return _index.Entries.Select(e => e.Id).ToList();

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dimension in query.Dimensions)
        {
            var dim = dimension.ToString().ToLowerInvariant();
            if (_index.FactPointers.TryGetValue(dim, out var pointers))
            {
                foreach (var pointer in pointers)
                    candidates.Add(pointer.EntryId);
            }
        }

        return candidates.ToList();
    }

    private List<string> RankCandidates(WikiQuery query, List<string> candidateIds)
    {
        var textQuery = query.TextQuery?.Trim() ?? string.Empty;
        var terms = Tokenize(textQuery);
        var scored = new List<(string EntryId, double Score)>(candidateIds.Count);

        foreach (var id in candidateIds)
        {
            var entry = _index.Entries.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                continue;

            if (!string.IsNullOrWhiteSpace(textQuery))
            {
                var haystack =
                    $"{entry.Subject} {entry.Summary} {string.Join(' ', entry.Aliases)} {string.Join(' ', entry.Tags)} {string.Join(' ', entry.FactKeys)}";
                var textScore = ComputeTextMatchScore(haystack, terms);
                if (textScore <= 0)
                    continue;

                scored.Add((id, (textScore * 0.5) + (entry.MaxConfidence * 0.3) + RecencyScore(entry.LastConfirmed) * 0.2));
            }
            else
            {
                scored.Add((id, (entry.MaxConfidence * 0.7) + (RecencyScore(entry.LastConfirmed) * 0.3)));
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Select(s => s.EntryId)
            .ToList();
    }

    private static double ComputeTextMatchScore(string haystack, HashSet<string> terms)
    {
        if (terms.Count == 0)
            return 1.0;

        var textTokens = Tokenize(haystack);
        if (textTokens.Count == 0)
            return 0.0;

        var overlap = terms.Count(term => textTokens.Contains(term));
        return (double)overlap / terms.Count;
    }

    private static double RecencyScore(DateTimeOffset lastConfirmed)
    {
        var days = (DateTimeOffset.UtcNow - lastConfirmed).TotalDays;
        return Math.Max(0, 1 - (days / 180.0));
    }

    private static bool MatchesQuery(WikiEntry entry, WikiQuery query)
    {
        if (query.MinConfidence > 0 && entry.Facts.All(f => f.Confidence < query.MinConfidence))
            return false;

        if (query.MaxAge.HasValue && entry.LastAccessed < DateTimeOffset.UtcNow - query.MaxAge.Value)
            return false;

        return string.IsNullOrEmpty(query.TextQuery) || MatchesText(entry, query.TextQuery);
    }

    private static bool MatchesText(WikiEntry entry, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        if (entry.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(entry.Summary) && entry.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            entry.Facts.Any(f => f.Claim.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var queryTokens = Tokenize(query);
        var entryTokens = Tokenize($"{entry.Subject} {entry.Summary} {string.Join(' ', entry.Facts.Select(f => f.Claim))}");
        return queryTokens.Any(entryTokens.Contains);
    }

    private static HashSet<string> Tokenize(string text)
    {
        return text
            .ToLowerInvariant()
            .Split(
                [' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 2)
            .ToHashSet();
    }

    private WikiIndex LoadOrRebuildIndex()
    {
        var loaded = TryLoadIndex();
        if (loaded is not null && loaded.Version == IndexVersion)
            return loaded;

        var rebuilt = RebuildIndexFromMarkdown();
        PersistIndex(rebuilt);
        return rebuilt;
    }

    private WikiIndex? TryLoadIndex()
    {
        if (!File.Exists(_indexPath))
            return null;

        try
        {
            var json = File.ReadAllText(_indexPath);
            return JsonSerializer.Deserialize<WikiIndex>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load wiki index from {Path}", _indexPath);
            return null;
        }
    }

    private WikiIndex ReloadIndexNoThrow()
    {
        return TryLoadIndex() ?? _index;
    }

    private void PersistIndex(WikiIndex index)
    {
        var json = JsonSerializer.Serialize(index with { Version = IndexVersion, BuiltAt = DateTimeOffset.UtcNow }, _jsonOptions);
        WriteAtomic(_indexPath, json);
    }

    private WikiIndex RebuildIndexFromMarkdown()
    {
        var entries = new List<WikiIndexEntry>();
        var pointers = BuildEmptyFactPointers();
        var pointerDedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dimension in Enum.GetValues<WikiDimension>())
        {
            var dim = dimension.ToString().ToLowerInvariant();
            var dimDir = Path.Combine(_basePath, dim);
            if (!Directory.Exists(dimDir))
                continue;

            foreach (var file in Directory.GetFiles(dimDir, "*.md"))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var fallbackId = $"{dim}-{Path.GetFileNameWithoutExtension(file)}";
                    var entry = ParseMarkdown(content, fallbackId);
                    if (entry is null)
                        continue;

                    var relativePath = Path.GetRelativePath(_basePath, file).Replace('\\', '/');
                    var factKeys = entry.Facts
                        .Select(f => f.NormalizedKey ?? $"{entry.Id}|{WikiFactMapper.NormalizeClaim(f.Claim)}")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    entries.Add(new WikiIndexEntry
                    {
                        Id = entry.Id,
                        Dimension = entry.Dimension.ToString().ToLowerInvariant(),
                        Subject = entry.Subject,
                        NormalizedSubject = WikiFactMapper.NormalizeClaim(entry.Subject),
                        Summary = entry.Summary,
                        Aliases = entry.Aliases.ToList(),
                        Tags = entry.Tags.ToList(),
                        FilePath = relativePath,
                        FactCount = entry.Facts.Count,
                        MaxConfidence = entry.Facts.Count == 0 ? 0 : entry.Facts.Max(f => f.Confidence),
                        LastConfirmed = entry.Facts.Count == 0 ? DateTimeOffset.MinValue : entry.Facts.Max(f => f.LastConfirmed),
                        Sources = entry.Facts.Select(f => f.Source).Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>().Distinct().ToList(),
                        FactKeys = factKeys
                    });

                    foreach (var fact in entry.Facts)
                    {
                        var factKey = fact.NormalizedKey ?? $"{entry.Id}|{WikiFactMapper.NormalizeClaim(fact.Claim)}";
                        foreach (var factDimension in GetFactDimensions(entry.Dimension, fact))
                        {
                            var pointerKey = $"{factDimension}:{entry.Id}:{factKey}";
                            if (!pointerDedup.Add(pointerKey))
                                continue;

                            pointers[factDimension].Add(new WikiFactPointer
                            {
                                EntryId = entry.Id,
                                FactKey = factKey
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed indexing wiki file {File}", file);
                }
            }
        }

        return new WikiIndex
        {
            Version = IndexVersion,
            BuiltAt = DateTimeOffset.UtcNow,
            Entries = entries,
            FactPointers = pointers
        };
    }

    private WikiIndex UpsertIndexEntry(WikiIndex current, WikiEntry entry)
    {
        var updated = RemoveIndexEntry(current, entry.Id);
        var entries = updated.Entries.ToList();
        var pointers = ClonePointers(updated.FactPointers);

        var factKeys = entry.Facts
            .Select(f => f.NormalizedKey ?? $"{entry.Id}|{WikiFactMapper.NormalizeClaim(f.Claim)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var indexEntry = new WikiIndexEntry
        {
            Id = entry.Id,
            Dimension = entry.Dimension.ToString().ToLowerInvariant(),
            Subject = entry.Subject,
            NormalizedSubject = WikiFactMapper.NormalizeClaim(entry.Subject),
            Summary = entry.Summary,
            Aliases = entry.Aliases.ToList(),
            Tags = entry.Tags.ToList(),
            FilePath = Path.GetRelativePath(_basePath, ResolvePathForWrite(entry)).Replace('\\', '/'),
            FactCount = entry.Facts.Count,
            MaxConfidence = entry.Facts.Count == 0 ? 0 : entry.Facts.Max(f => f.Confidence),
            LastConfirmed = entry.Facts.Count == 0 ? DateTimeOffset.MinValue : entry.Facts.Max(f => f.LastConfirmed),
            Sources = entry.Facts.Select(f => f.Source).Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>().Distinct().ToList(),
            FactKeys = factKeys
        };
        entries.Add(indexEntry);

        for (var i = 0; i < entry.Facts.Count; i++)
        {
            var fact = entry.Facts[i];
            var factKey = fact.NormalizedKey ?? $"{entry.Id}|{WikiFactMapper.NormalizeClaim(fact.Claim)}";
            foreach (var dimension in GetFactDimensions(entry.Dimension, fact))
            {
                pointers[dimension].Add(new WikiFactPointer
                {
                    EntryId = entry.Id,
                    FactKey = factKey
                });
            }
        }

        return new WikiIndex
        {
            Version = IndexVersion,
            BuiltAt = DateTimeOffset.UtcNow,
            Entries = entries,
            FactPointers = pointers
        };
    }

    private static WikiIndex RemoveIndexEntry(WikiIndex current, string entryId)
    {
        var entries = current.Entries
            .Where(e => !e.Id.Equals(entryId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pointers = ClonePointers(current.FactPointers);
        foreach (var dim in pointers.Keys.ToList())
        {
            pointers[dim] = pointers[dim]
                .Where(pointer => !pointer.EntryId.Equals(entryId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return new WikiIndex
        {
            Version = IndexVersion,
            BuiltAt = DateTimeOffset.UtcNow,
            Entries = entries,
            FactPointers = pointers
        };
    }

    private static IEnumerable<string> GetFactDimensions(WikiDimension primaryDimension, WikiFact fact)
    {
        var dimensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            primaryDimension.ToString().ToLowerInvariant()
        };

        if (fact.Context is null)
            return dimensions;

        if (!string.IsNullOrWhiteSpace(fact.Context.Who)) dimensions.Add("who");
        if (!string.IsNullOrWhiteSpace(fact.Context.What)) dimensions.Add("what");
        if (!string.IsNullOrWhiteSpace(fact.Context.When)) dimensions.Add("when");
        if (!string.IsNullOrWhiteSpace(fact.Context.Where)) dimensions.Add("where");
        if (!string.IsNullOrWhiteSpace(fact.Context.Why)) dimensions.Add("why");
        if (!string.IsNullOrWhiteSpace(fact.Context.How)) dimensions.Add("how");
        return dimensions;
    }

    private static Dictionary<string, List<WikiFactPointer>> BuildEmptyFactPointers()
    {
        return new Dictionary<string, List<WikiFactPointer>>(StringComparer.OrdinalIgnoreCase)
        {
            ["who"] = [],
            ["what"] = [],
            ["when"] = [],
            ["where"] = [],
            ["why"] = [],
            ["how"] = []
        };
    }

    private static Dictionary<string, List<WikiFactPointer>> ClonePointers(Dictionary<string, List<WikiFactPointer>> source)
    {
        var clone = BuildEmptyFactPointers();
        foreach (var (key, value) in source)
        {
            if (!clone.ContainsKey(key))
                clone[key] = [];
            clone[key] = value.ToList();
        }

        return clone;
    }

    private static void WriteAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{path}.tmp-{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
    }

    private void EnsureDirectories()
    {
        foreach (var dim in Enum.GetValues<WikiDimension>())
            Directory.CreateDirectory(Path.Combine(_basePath, dim.ToString().ToLowerInvariant()));
        Directory.CreateDirectory(_metaPath);
    }
}
