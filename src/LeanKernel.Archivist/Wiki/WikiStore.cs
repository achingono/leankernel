using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Filesystem-backed 5W1H wiki store using markdown files with YAML frontmatter.
/// Each entry is a .md file organized by dimension: data/wiki/{who,what,where,when,why,how}/.
/// </summary>
public sealed class WikiStore : IWikiStore
{
    private readonly string _basePath;
    private readonly ILogger<WikiStore> _logger;

    private static readonly Regex FactLineRegex = new(
        @"^-\s+(?<claim>.+?)(?:\s*<!--\{(?<meta>[^}]*)\}-->)?$",
        RegexOptions.Compiled);

    private static readonly Regex RelatedLinkRegex = new(
        @"^-\s+\[(?<text>[^\]]+)\]\((?<path>[^)]+)\)$",
        RegexOptions.Compiled);

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

        var content = await File.ReadAllTextAsync(path, ct);
        return ParseMarkdown(content, entryId);
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
                if (!MatchesQuery(entry, query))
                    continue;

                results.Add(entry);
                if (results.Count >= query.MaxResults) return results;
            }
        }

        return results;
    }

    private static bool MatchesQuery(WikiEntry entry, WikiQuery query)
    {
        if (query.MinConfidence > 0 && entry.Facts.All(f => f.Confidence < query.MinConfidence))
            return false;

        if (query.MaxAge.HasValue && entry.LastAccessed < DateTimeOffset.UtcNow - query.MaxAge.Value)
            return false;

        return string.IsNullOrEmpty(query.TextQuery) || MatchesText(entry, query.TextQuery);
    }

    public async Task UpsertAsync(WikiEntry entry, CancellationToken ct)
    {
        var path = ResolvePath(entry.Id);
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var markdown = SerializeToMarkdown(entry);
        await File.WriteAllTextAsync(path, markdown, ct);
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
        foreach (var file in Directory.GetFiles(dimDir, "*.md"))
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var fileName = Path.GetFileNameWithoutExtension(file);
                var fallbackId = $"{dimension.ToString().ToLowerInvariant()}-{fileName}";
                var entry = ParseMarkdown(content, fallbackId);
                if (entry is not null) entries.Add(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse wiki entry: {File}", file);
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

        // YAML frontmatter
        sb.AppendLine("---");
        sb.AppendLine($"id: {entry.Id}");
        sb.AppendLine($"dimension: {entry.Dimension.ToString().ToLowerInvariant()}");
        sb.AppendLine($"subject: {entry.Subject}");
        sb.AppendLine($"lastAccessed: {entry.LastAccessed:O}");
        sb.AppendLine($"accessCount: {entry.AccessCount}");
        sb.AppendLine("---");
        sb.AppendLine();

        // Heading
        sb.AppendLine($"# {entry.Subject}");
        sb.AppendLine();

        // Facts as list items with metadata in HTML comments
        foreach (var fact in entry.Facts)
        {
            sb.Append($"- {fact.Claim}");
            var meta = FormatFactMeta(fact);
            if (!string.IsNullOrEmpty(meta))
                sb.Append($" <!--{{{meta}}}-->");
            sb.AppendLine();
        }

        // Related section
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
        if (string.IsNullOrWhiteSpace(content)) return null;

        var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        if (!TryReadFrontmatter(lines, out var endIdx, out var frontmatter))
            return null;

        var id = frontmatter.GetValueOrDefault("id", fallbackId);
        var dimensionStr = frontmatter.GetValueOrDefault("dimension", "what");
        var subject = frontmatter.GetValueOrDefault("subject", "Unknown");
        var lastAccessed = ParseDateTimeOffset(frontmatter.GetValueOrDefault("lastAccessed", ""));
        var accessCount = int.TryParse(frontmatter.GetValueOrDefault("accessCount", "0"), out var ac) ? ac : 0;

        if (!Enum.TryParse<WikiDimension>(dimensionStr, ignoreCase: true, out var dimension))
            dimension = WikiDimension.What;

        // Parse body (after frontmatter)
        var bodyLines = lines.Skip(endIdx + 1).ToList();
        var (facts, relations) = ParseBodyLines(bodyLines, dimension);

        return new WikiEntry
        {
            Id = id,
            Dimension = dimension,
            Subject = subject,
            Facts = facts,
            Relations = relations,
            LastAccessed = lastAccessed,
            AccessCount = accessCount
        };
    }

    private static bool TryReadFrontmatter(
        List<string> lines,
        out int endIdx,
        out Dictionary<string, string> frontmatter)
    {
        endIdx = -1;
        frontmatter = [];

        if (lines.Count < 3 || lines[0] != "---")
            return false;

        endIdx = lines.IndexOf("---", 1);
        if (endIdx < 0)
            return false;

        frontmatter = ParseFrontmatter(lines.Skip(1).Take(endIdx - 1));
        return true;
    }

    private static (List<WikiFact> Facts, List<string> Relations) ParseBodyLines(
        List<string> bodyLines,
        WikiDimension dimension)
    {
        var facts = new List<WikiFact>();
        var relations = new List<string>();
        var inRelatedSection = false;

        foreach (var line in bodyLines)
        {
            if (line.StartsWith("## Related", StringComparison.OrdinalIgnoreCase))
            {
                inRelatedSection = true;
                continue;
            }

            if (line.StartsWith("## "))
            {
                inRelatedSection = false;
                continue;
            }

            if (inRelatedSection)
                AddRelation(line, dimension, relations);
            else
                AddFact(line, facts);
        }

        return (facts, relations);
    }

    private static void AddRelation(string line, WikiDimension dimension, List<string> relations)
    {
        var relMatch = RelatedLinkRegex.Match(line);
        if (!relMatch.Success)
            return;

        var linkPath = relMatch.Groups["path"].Value;
        var relationId = ExtractRelationId(linkPath, dimension);
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

    private static Dictionary<string, string> ParseFrontmatter(IEnumerable<string> lines)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;
            var key = line[..colonIdx].Trim();
            var value = line[(colonIdx + 1)..].Trim();
            dict[key] = value;
        }
        return dict;
    }

    private static WikiFact ParseFact(string claim, string metaStr)
    {
        var fact = new WikiFact
        {
            Claim = claim,
            Confidence = 0.5,
            LastConfirmed = DateTimeOffset.UtcNow,
            EstimatedTokens = (int)Math.Ceiling(claim.Length / 4.0)
        };

        if (string.IsNullOrWhiteSpace(metaStr)) return fact;

        var pairs = metaStr.Split(',').Select(p => p.Trim());
        foreach (var pair in pairs)
        {
            var colonIdx = pair.IndexOf(':');
            if (colonIdx <= 0) continue;
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
                    var confirmed = ParseDateTimeOffset(value);
                    fact.LastConfirmed = confirmed;
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
        var parts = new List<string>();
        parts.Add($"confidence: {fact.Confidence.ToString("F2", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrEmpty(fact.Source))
            parts.Add($"source: {fact.Source}");
        parts.Add($"confirmed: {fact.LastConfirmed:yyyy-MM-dd}");
        if (fact.EstimatedTokens > 0)
            parts.Add($"tokens: {fact.EstimatedTokens}");
        return string.Join(", ", parts);
    }

    private static (string Text, string Path) ResolveRelationLink(string relationId, WikiDimension currentDimension)
    {
        // relationId format: "what-project-atlas" → dimension "what", name "project-atlas"
        var parts = relationId.Split('-', 2);
        var dim = parts.Length > 0 ? parts[0] : "what";
        var name = parts.Length > 1 ? parts[1] : relationId;
        var displayName = name.Replace('-', ' ');
        displayName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(displayName);

        var currentDimStr = currentDimension.ToString().ToLowerInvariant();
        var linkPath = dim == currentDimStr
            ? $"./{name}.md"
            : $"../{dim}/{name}.md";

        return (displayName, linkPath);
    }

    private static string ExtractRelationId(string linkPath, WikiDimension currentDimension)
    {
        // "../what/project-atlas.md" → "what-project-atlas"
        // "./alice-smith.md" → "who-alice-smith" (same dimension, prefixed with current)
        var normalized = linkPath.Replace('\\', '/');
        var parts = normalized.Split('/');
        var fileName = Path.GetFileNameWithoutExtension(parts[^1]);

        if (parts.Length >= 2 && parts[^2] != ".")
            return $"{parts[^2]}-{fileName}";

        // Same-dimension link (starts with ./) — prefix with current dimension
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
            var match = mergedFacts.FindIndex(f =>
                f.Claim.Equals(newFact.Claim, StringComparison.OrdinalIgnoreCase));

            if (match >= 0)
            {
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
        // entryId format: "who-alice-smith" → wiki/who/alice-smith.md
        var parts = entryId.Split('-', 2);
        var dimension = parts.Length > 0 ? parts[0] : "what";
        var name = parts.Length > 1 ? parts[1] : entryId;
        return Path.Combine(_basePath, dimension, $"{name}.md");
    }

    private static bool MatchesText(WikiEntry entry, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        if (entry.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            entry.Facts.Any(f => f.Claim.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
            return false;

        var entryTokens = Tokenize($"{entry.Subject} {string.Join(' ', entry.Facts.Select(f => f.Claim))}");
        if (entryTokens.Count == 0)
            return false;

        return queryTokens.Any(entryTokens.Contains);
    }

    private static HashSet<string> Tokenize(string text)
    {
        return text
            .ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 2)
            .ToHashSet();
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
