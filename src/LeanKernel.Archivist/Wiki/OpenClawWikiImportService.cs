using System.Diagnostics;
using System.Security.Cryptography;
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
/// Imports OpenClaw wiki pages from a remote host and maps them into canonical LeanKernel wiki entries.
/// </summary>
public sealed class OpenClawWikiImportService : IWikiImportService
{
    private static readonly Regex HeadingRegex = new(@"^##\s+(WHO|WHAT|WHEN|WHERE|WHY|HOW)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex WikiLinkRegex = new(@"\[\[([^\]]+)\]\]", RegexOptions.Compiled);

    private static readonly HashSet<string> IgnoredWikiFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "index.md",
        "log.md",
        "WIKI-SCHEMA.md",
        "WIKI-SCHEMA.md.v1"
    };

    private static readonly HashSet<string> ExcludedTopLevelFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "sources"
    };

    private readonly IWikiStore _wikiStore;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<OpenClawWikiImportService> _logger;
    private readonly OllamaOpenClawExtractor? _ollamaExtractor;

    public OpenClawWikiImportService(
        IWikiStore wikiStore,
        IOptions<LeanKernelConfig> config,
        ILogger<OpenClawWikiImportService> logger,
        OllamaOpenClawExtractor? ollamaExtractor = null)
    {
        _wikiStore = wikiStore;
        _config = config.Value;
        _logger = logger;
        _ollamaExtractor = ollamaExtractor;
    }

    /// <inheritdoc />
    public async Task<OpenClawImportResult> ImportOpenClawAsync(OpenClawImportRequest request, CancellationToken ct)
    {
        var openClawConfig = _config.Wiki.OpenClawImport;
        if (!openClawConfig.Enabled)
        {
            throw new InvalidOperationException("OpenClaw wiki import is disabled in configuration.");
        }

        var runId = request.RunId ?? DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var stagingRoot = Path.Combine(
            _config.Wiki.BasePath,
            _config.Wiki.MetaFolder,
            openClawConfig.StagingFolder,
            runId);
        var stagedWikiRoot = Path.Combine(stagingRoot, "wiki");
        var stagedSessionsRoot = Path.Combine(stagingRoot, "agents");
        Directory.CreateDirectory(stagingRoot);

        if (!request.SkipRemoteSync)
        {
            await SyncRemoteDataAsync(stagingRoot, openClawConfig, ct);
        }

        var wikiPages = LoadWikiPages(stagedWikiRoot);
        var sessionTexts = await LoadSessionTextsAsync(stagedSessionsRoot, ct);
        var manifest = BuildManifest(stagingRoot, wikiPages.Count, sessionTexts.Count);

        // Determine extraction strategy
        var strategy = request.Strategy;
        
        // Extract candidates using selected strategy
        var candidates = strategy == WikiExtractionStrategy.LLM && _ollamaExtractor != null
            ? await ExtractWithLLMAsync(wikiPages, sessionTexts, ct)
            : BuildCandidateFacts(wikiPages);
            
        var grouped = candidates.GroupBy(c => c.EntryId, StringComparer.OrdinalIgnoreCase);

        var quarantined = new List<QuarantinedFact>();
        var importedFacts = 0;
        var upsertedEntries = 0;

        foreach (var group in grouped)
        {
            ct.ThrowIfCancellationRequested();
            var first = group.First();
            var existing = await _wikiStore.GetAsync(first.EntryId, ct);
            var mergedFacts = existing?.Facts.ToList() ?? [];
            var existingKeys = new HashSet<string>(
                mergedFacts.Select(f => f.NormalizedKey ?? $"{first.EntryId}|{WikiFactMapper.NormalizeClaim(f.Claim)}"),
                StringComparer.OrdinalIgnoreCase);

            var importedForEntry = 0;
            foreach (var candidate in group)
            {
                var corroboration = await CorroborateAsync(candidate.Claim, sessionTexts, ct);
                if (!corroboration.Corroborated)
                {
                    quarantined.Add(new QuarantinedFact(
                        candidate.SourcePath,
                        candidate.EntryId,
                        candidate.Claim,
                        "uncorroborated"));
                    continue;
                }

                var normalizedClaim = WikiFactMapper.NormalizeClaim(candidate.Claim);
                var normalizedKey = $"{candidate.EntryId}|{normalizedClaim}";
                if (existingKeys.Contains(normalizedKey))
                {
                    continue;
                }

                if (HasPolarityConflict(mergedFacts, normalizedClaim))
                {
                    quarantined.Add(new QuarantinedFact(
                        candidate.SourcePath,
                        candidate.EntryId,
                        candidate.Claim,
                        "conflict-with-existing-fact"));
                    continue;
                }

                mergedFacts.Add(new WikiFact
                {
                    Claim = candidate.Claim,
                    Context = candidate.Context,
                    SourceQuote = corroboration.SourceSnippet,
                    NormalizedKey = normalizedKey,
                    Tags = candidate.Tags,
                    Confidence = Math.Clamp(0.65 + corroboration.MatchStrength, 0.0, 1.0),
                    Source = $"openclaw:{candidate.SourcePath}#{corroboration.Source}",
                    LastConfirmed = DateTimeOffset.UtcNow,
                    EstimatedTokens = (int)Math.Ceiling(candidate.Claim.Length / 4.0)
                });
                existingKeys.Add(normalizedKey);
                importedForEntry++;
            }

            if (importedForEntry == 0)
            {
                continue;
            }

            importedFacts += importedForEntry;
            var entry = new WikiEntry
            {
                Id = first.EntryId,
                Dimension = first.Dimension,
                Subject = first.Subject,
                Summary = first.Summary,
                Aliases = first.Aliases,
                Tags = first.Tags,
                Facts = mergedFacts,
                Relations = first.Relations
            };

            if (!request.DryRun)
            {
                await _wikiStore.UpsertAsync(entry, ct);
            }

            upsertedEntries++;
        }

        var audit = new OpenClawImportAudit
        {
            RunId = runId,
            DryRun = request.DryRun,
            Manifest = manifest,
            PagesProcessed = wikiPages.Count,
            FactsExtracted = candidates.Count,
            FactsImported = importedFacts,
            EntriesUpserted = upsertedEntries,
            Quarantined = quarantined
        };

        var auditPath = Path.Combine(stagingRoot, "audit.json");
        await File.WriteAllTextAsync(
            auditPath,
            JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }),
            ct);

        _logger.LogInformation(
            "OpenClaw import run {RunId} completed: {Pages} pages, {Extracted} facts, {Imported} imported, {Quarantined} quarantined, dryRun={DryRun}",
            runId,
            wikiPages.Count,
            candidates.Count,
            importedFacts,
            quarantined.Count,
            request.DryRun);

        return new OpenClawImportResult(
            runId,
            request.DryRun,
            request.Strategy,
            wikiPages.Count,
            candidates.Count,
            importedFacts,
            quarantined.Count,
            upsertedEntries,
            auditPath);
    }

    private static bool HasPolarityConflict(List<WikiFact> existingFacts, string normalizedClaim)
    {
        var incomingNegated = normalizedClaim.Contains(" not ", StringComparison.OrdinalIgnoreCase) ||
                              normalizedClaim.StartsWith("not ", StringComparison.OrdinalIgnoreCase);
        foreach (var fact in existingFacts)
        {
            var existingNormalized = WikiFactMapper.NormalizeClaim(fact.Claim);
            var overlap = TokenOverlap(normalizedClaim, existingNormalized);
            if (overlap < 4)
            {
                continue;
            }

            var existingNegated = existingNormalized.Contains(" not ", StringComparison.OrdinalIgnoreCase) ||
                                  existingNormalized.StartsWith("not ", StringComparison.OrdinalIgnoreCase);
            if (incomingNegated != existingNegated)
            {
                return true;
            }
        }

        return false;
    }

    private static int TokenOverlap(string a, string b)
    {
        var aSet = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bSet = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        aSet.IntersectWith(bSet);
        return aSet.Count;
    }

    private async Task<CorroborationResult> CorroborateAsync(string claim, List<string> sessionTexts, CancellationToken ct)
    {
        var sessionCorroboration = CorroborateAgainstSessions(claim, sessionTexts);
        if (sessionCorroboration.Corroborated)
        {
            return sessionCorroboration;
        }

        if (_ollamaExtractor is null)
        {
            return CorroborationResult.NotCorroborated;
        }

        var qdrantMatches = await _ollamaExtractor.QueryQdrantForCorroborationAsync(claim, ct);
        if (qdrantMatches.Count == 0)
        {
            return CorroborationResult.NotCorroborated;
        }

        return new CorroborationResult(true, 0.20, qdrantMatches[0], "qdrant");
    }

    private static CorroborationResult CorroborateAgainstSessions(string claim, List<string> sessionTexts)
    {
        var normalizedClaim = WikiFactMapper.NormalizeClaim(claim);
        var claimTokens = normalizedClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (claimTokens.Length < 4 || sessionTexts.Count == 0)
        {
            return CorroborationResult.NotCorroborated;
        }

        var bestOverlap = 0;
        string? bestSnippet = null;
        foreach (var text in sessionTexts)
        {
            var normalizedText = WikiFactMapper.NormalizeClaim(text);
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                continue;
            }

            if (normalizedText.Contains(normalizedClaim, StringComparison.OrdinalIgnoreCase))
            {
                return new CorroborationResult(true, 0.35, text[..Math.Min(text.Length, 220)], "session_log");
            }

            var overlap = TokenOverlap(normalizedClaim, normalizedText);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                bestSnippet = text[..Math.Min(text.Length, 220)];
            }
        }

        if (bestOverlap >= 4)
        {
            var strength = Math.Min(bestOverlap / 12.0, 0.30);
            return new CorroborationResult(true, strength, bestSnippet, "session_log");
        }

        return CorroborationResult.NotCorroborated;
    }

    private static OpenClawImportManifest BuildManifest(string stagingRoot, int wikiPageCount, int sessionSnippetCount)
    {
        var files = Directory.Exists(stagingRoot)
            ? Directory
                .EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories)
                .Select(path => new OpenClawImportManifestFile
                {
                    Path = path.Replace(stagingRoot, string.Empty).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'),
                    Sha256 = ComputeSha256(path)
                })
                .ToList()
            : [];

        return new OpenClawImportManifest
        {
            WikiPageCount = wikiPageCount,
            SessionSnippetCount = sessionSnippetCount,
            Files = files
        };
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static List<CandidateFact> BuildCandidateFacts(List<OpenClawPage> pages)
    {
        var candidates = new List<CandidateFact>();
        foreach (var page in pages)
        {
            var subject = GetSubject(page.Title, page.RelativePath);
            var slug = WikiFactMapper.Slugify(subject);
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            var baseTags = new List<string> { "openclaw-import", page.Domain };
            foreach (var (sectionName, sectionBody) in page.Sections)
            {
                if (!TryMapDimension(sectionName, out var sectionDimension))
                {
                    continue;
                }

                var claims = ExtractClaims(sectionBody);
                var context = BuildContext(page.Sections);
                foreach (var claim in claims)
                {
                    candidates.Add(new CandidateFact
                    {
                        EntryId = $"{sectionDimension.ToString().ToLowerInvariant()}-{slug}",
                        Dimension = sectionDimension,
                        Subject = subject,
                        Claim = claim,
                        Summary = page.Essence,
                        SourcePath = page.RelativePath,
                        Context = context,
                        Tags = baseTags.Union([sectionDimension.ToString().ToLowerInvariant()]).ToList(),
                        Relations = page.RelatedLinks,
                        Aliases = BuildAliases(subject, page.RelativePath)
                    });
                }
            }
        }

        return candidates;
    }

    private async Task<List<CandidateFact>> ExtractWithLLMAsync(
        List<OpenClawPage> pages,
        List<string> sessionTexts,
        CancellationToken ct)
    {
        if (_ollamaExtractor == null)
        {
            _logger.LogWarning("LLM extraction requested but OllamaOpenClawExtractor not available, falling back to deterministic");
            return BuildCandidateFacts(pages);
        }

        var candidates = new List<CandidateFact>();
        var baseTags = new List<string> { "openclaw-import", "llm-extracted" };

        foreach (var page in pages)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Build page content from sections
                var pageContentBuilder = new StringBuilder();
                pageContentBuilder.AppendLine($"# {page.Title}");
                if (!string.IsNullOrWhiteSpace(page.Essence))
                    pageContentBuilder.AppendLine($"> {page.Essence}");
                foreach (var (section, content) in page.Sections)
                {
                    pageContentBuilder.AppendLine($"\n## {section}");
                    pageContentBuilder.AppendLine(content);
                }
                var pageContent = pageContentBuilder.ToString();

                // Prepare session log snippets for this page's topic
                var relevantSessionSnippets = SessionLogSnippets(page.Title, page.Sections, sessionTexts).Take(10).ToList();

                // Call LLM extraction
                var extracted = await _ollamaExtractor.ExtractFromPageAsync(
                    page.Title,
                    pageContent,
                    page.Sections,
                    relevantSessionSnippets,
                    ct);

                var subject = GetSubject(page.Title, page.RelativePath);
                var slug = WikiFactMapper.Slugify(subject);
                var context = BuildContext(page.Sections);
                var pageTags = baseTags.Union([page.Domain]).ToList();

                foreach (var fact in extracted)
                {
                    // Map Ollama fact to CandidateFact
                    candidates.Add(new CandidateFact
                    {
                        EntryId = fact.EntryId,
                        Dimension = Enum.Parse<WikiDimension>(fact.Dimension, ignoreCase: true),
                        Subject = fact.Subject,
                        Claim = fact.Claim,
                        Summary = page.Essence,
                        SourcePath = page.RelativePath,
                        Context = fact.Context.ToWikiFactContext() ?? context,
                        Tags = pageTags.Union(fact.Tags).ToList(),
                        Relations = page.RelatedLinks,
                        Aliases = BuildAliases(subject, page.RelativePath)
                    });
                }

                _logger.LogDebug("LLM extraction: {PageTitle} → {FactCount} facts", page.Title, extracted.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM extraction failed for page {PageTitle}, skipping", page.Title);
            }
        }

        return candidates;
    }

    private static List<string> SessionLogSnippets(
        string pageTitle,
        Dictionary<string, string> sections,
        List<string> allSessionTexts)
    {
        // Simple heuristic: return session snippets that mention page title keywords
        var keywords = pageTitle.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(k => k.Length > 3)
            .Select(k => k.ToLowerInvariant())
            .ToHashSet();

         return allSessionTexts
            .Where(snippet => keywords.Any(k => snippet.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static List<string> BuildAliases(string subject, string relativePath)
    {
        var aliasFromPath = Path.GetFileNameWithoutExtension(relativePath).Replace('-', ' ');
        return new[] { aliasFromPath }
            .Where(alias => !string.Equals(alias, subject, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static WikiFactContext BuildContext(Dictionary<string, string> sections)
    {
        return new WikiFactContext
        {
            Who = NormalizeOptional(sections.GetValueOrDefault("WHO")),
            What = NormalizeOptional(sections.GetValueOrDefault("WHAT")),
            When = NormalizeOptional(sections.GetValueOrDefault("WHEN")),
            Where = NormalizeOptional(sections.GetValueOrDefault("WHERE")),
            Why = NormalizeOptional(sections.GetValueOrDefault("WHY")),
            How = NormalizeOptional(sections.GetValueOrDefault("HOW"))
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = string.Join(" ", value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(static l => l.Trim()));
        return compact.Length > 700 ? compact[..700] : compact;
    }

    private static List<string> ExtractClaims(string sectionBody)
    {
        var claims = new List<string>();
        var lines = sectionBody
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static l => l.Trim())
            .Where(static l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        foreach (var line in lines)
        {
            if (line.StartsWith("###", StringComparison.Ordinal) || line.StartsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            var content = line
                .TrimStart('-', '*')
                .Trim();
            content = Regex.Replace(content, @"^\d+\.\s+", string.Empty);
            content = MarkdownLinkRegex.Replace(content, "$1");
            content = WikiLinkRegex.Replace(content, "$1");

            if (content.Length < 16)
            {
                continue;
            }

            claims.Add(content);
        }

        return claims
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryMapDimension(string sectionName, out WikiDimension dimension)
    {
        return Enum.TryParse(sectionName, true, out dimension);
    }

    private static string GetSubject(string title, string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            var parts = title.Split('—', 2, StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                return parts[0];
            }
        }

        return Path.GetFileNameWithoutExtension(relativePath).Replace('-', ' ');
    }

    private static List<OpenClawPage> LoadWikiPages(string stagedWikiRoot)
    {
        if (!Directory.Exists(stagedWikiRoot))
        {
            return [];
        }

        var pages = new List<OpenClawPage>();
        var allFiles = Directory.EnumerateFiles(stagedWikiRoot, "*.md", SearchOption.AllDirectories);
        foreach (var file in allFiles)
        {
            var relativePath = Path.GetRelativePath(stagedWikiRoot, file).Replace('\\', '/');
            var topFolder = relativePath.Split('/', 2)[0];
            if (ExcludedTopLevelFolders.Contains(topFolder))
            {
                continue;
            }

            var fileName = Path.GetFileName(relativePath);
            if (IgnoredWikiFiles.Contains(fileName))
            {
                continue;
            }

            var markdown = File.ReadAllText(file);
            var parsed = ParseOpenClawPage(markdown, relativePath);
            if (parsed is not null)
            {
                pages.Add(parsed);
            }
        }

        return pages;
    }

    private static OpenClawPage? ParseOpenClawPage(string markdown, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var lines = markdown.Split('\n').Select(static l => l.TrimEnd('\r')).ToList();
        var title = lines.FirstOrDefault(static l => l.StartsWith("# ", StringComparison.Ordinal))?.TrimStart('#', ' ').Trim() ?? string.Empty;
        var essence = lines.FirstOrDefault(static l => l.StartsWith("> ", StringComparison.Ordinal))?.Substring(2).Trim();

        var relatedLinks = new List<string>();
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentSection = string.Empty;
        var sectionBuilder = new StringBuilder();

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(currentSection))
            {
                return;
            }

            sections[currentSection] = sectionBuilder.ToString().Trim();
            sectionBuilder.Clear();
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var heading = HeadingRegex.Match(line);
            if (heading.Success)
            {
                Flush();
                currentSection = heading.Groups[1].Value.ToUpperInvariant();
                continue;
            }

            if (line.StartsWith("related:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in WikiLinkRegex.Matches(line))
                {
                    relatedLinks.Add(match.Groups[1].Value);
                }
            }

            if (!string.IsNullOrWhiteSpace(currentSection))
            {
                sectionBuilder.AppendLine(line);
            }
        }

        Flush();

        if (sections.Count == 0)
        {
            return null;
        }

        var domain = relativePath.Contains('/') ? relativePath[..relativePath.IndexOf('/')] : "context";
        return new OpenClawPage
        {
            RelativePath = relativePath,
            Domain = domain,
            Title = title,
            Essence = essence,
            Sections = sections,
            RelatedLinks = relatedLinks
        };
    }

    private async Task<List<string>> LoadSessionTextsAsync(string stagedSessionsRoot, CancellationToken ct)
    {
        if (!Directory.Exists(stagedSessionsRoot))
        {
            return [];
        }

        var texts = new HashSet<string>(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(stagedSessionsRoot, "*.json*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if (file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                await foreach (var line in File.ReadLinesAsync(file, ct))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    TryExtractTextFromJson(line, texts);
                }
                continue;
            }

            try
            {
                var raw = await File.ReadAllTextAsync(file, ct);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    TryExtractTextFromJson(raw, texts);
                }
            }
            catch (JsonException)
            {
                // ignore malformed non-canonical backup artifacts
            }
        }

        return texts
            .Where(static t => t.Length >= 20)
            .Select(static t => t.Length <= 900 ? t : t[..900])
            .Take(100_000)
            .ToList();
    }

    private static void TryExtractTextFromJson(string json, HashSet<string> target)
    {
        using var doc = JsonDocument.Parse(json);
        CollectText(doc.RootElement, target);
    }

    private static void CollectText(JsonElement element, HashSet<string> target)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        if (property.NameEquals("text") || property.NameEquals("content") || property.NameEquals("message"))
                        {
                            var value = property.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                target.Add(value.Trim());
                            }
                        }
                    }

                    CollectText(property.Value, target);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectText(item, target);
                }
                break;
            case JsonValueKind.String:
                var raw = element.GetString();
                if (!string.IsNullOrWhiteSpace(raw) && raw.Length >= 24)
                {
                    target.Add(raw.Trim());
                }
                break;
        }
    }

    private async Task SyncRemoteDataAsync(string stagingRoot, OpenClawImportConfig openClawConfig, CancellationToken ct)
    {
        var wikiArchive = Path.Combine(stagingRoot, "wiki.tar.gz");
        var sessionsArchive = Path.Combine(stagingRoot, "agents-sessions.tar.gz");

        await ExecuteProcessAsync(
            "bash",
            $"-lc \"ssh -o BatchMode=yes -o ConnectTimeout=15 -o StrictHostKeyChecking=no {EscapeShell(openClawConfig.RemoteHost)} 'cd {EscapeShell(Path.GetDirectoryName(openClawConfig.RemoteWikiPath) ?? "/")} && tar -czf - {EscapeShell(Path.GetFileName(openClawConfig.RemoteWikiPath))}' > {EscapeShell(wikiArchive)}\"",
            ct);

        await ExecuteProcessAsync(
            "bash",
            $"-lc \"ssh -o BatchMode=yes -o ConnectTimeout=15 -o StrictHostKeyChecking=no {EscapeShell(openClawConfig.RemoteHost)} 'cd {EscapeShell(openClawConfig.RemoteAgentsPath)} && tar -czf - */sessions/*.json*' > {EscapeShell(sessionsArchive)}\"",
            ct);

        Directory.CreateDirectory(Path.Combine(stagingRoot, "wiki"));
        Directory.CreateDirectory(Path.Combine(stagingRoot, "agents"));

        await ExecuteProcessAsync(
            "tar",
            $"-xzf {EscapeShell(wikiArchive)} -C {EscapeShell(stagingRoot)}",
            ct);

        await ExecuteProcessAsync(
            "tar",
            $"-xzf {EscapeShell(sessionsArchive)} -C {EscapeShell(Path.Combine(stagingRoot, "agents"))}",
            ct);
    }

    private async Task ExecuteProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process '{fileName} {arguments}' failed with exit code {process.ExitCode}. stderr: {stdErr}. stdout: {stdOut}");
        }
    }

    private static string EscapeShell(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'")}'";
    }

    private sealed record OpenClawPage
    {
        public string RelativePath { get; init; } = string.Empty;
        public string Domain { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string? Essence { get; init; }
        public Dictionary<string, string> Sections { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> RelatedLinks { get; init; } = [];
    }

    private sealed record CandidateFact
    {
        public string EntryId { get; init; } = string.Empty;
        public WikiDimension Dimension { get; init; }
        public string Subject { get; init; } = string.Empty;
        public string Claim { get; init; } = string.Empty;
        public string? Summary { get; init; }
        public string SourcePath { get; init; } = string.Empty;
        public WikiFactContext Context { get; init; } = new();
        public List<string> Tags { get; init; } = [];
        public List<string> Relations { get; init; } = [];
        public List<string> Aliases { get; init; } = [];
    }

    private sealed record CorroborationResult(bool Corroborated, double MatchStrength, string? SourceSnippet, string Source)
    {
        public static CorroborationResult NotCorroborated { get; } = new(false, 0.0, null, "none");
    }

    private sealed record QuarantinedFact(string SourcePath, string EntryId, string Claim, string Reason);

    private sealed record OpenClawImportAudit
    {
        public string RunId { get; init; } = string.Empty;
        public bool DryRun { get; init; }
        public OpenClawImportManifest Manifest { get; init; } = new();
        public int PagesProcessed { get; init; }
        public int FactsExtracted { get; init; }
        public int FactsImported { get; init; }
        public int EntriesUpserted { get; init; }
        public List<QuarantinedFact> Quarantined { get; init; } = [];
    }

    private sealed record OpenClawImportManifest
    {
        public int WikiPageCount { get; init; }
        public int SessionSnippetCount { get; init; }
        public List<OpenClawImportManifestFile> Files { get; init; } = [];
    }

    private sealed record OpenClawImportManifestFile
    {
        public string Path { get; init; } = string.Empty;
        public string Sha256 { get; init; } = string.Empty;
    }
}

/// <summary>
/// Extensions for converting between extraction models.
/// </summary>
internal static class ExtractionExtensions
{
    public static WikiFactContext? ToWikiFactContext(this OllamaFactContext context)
    {
        if (context == null)
            return null;

        return new WikiFactContext
        {
            Who = context.Who,
            What = context.What,
            When = context.When,
            Where = context.Where,
            Why = context.Why,
            How = context.How
        };
    }
}
