using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Identity;

/// <summary>
/// Deterministically cleans and updates engagement files from verified source documents.
/// </summary>
public sealed class EngagementFileMaintenanceService : IEngagementFileMaintenanceService
{
    private static readonly string[] EngagementFileNames = ["AGENTS.md", "SELF.md", "USER.md"];
    private static readonly string[] PlaceholderMarkers =
    [
        "Your role or title",
        "Preferred response length: concise/moderate/detailed",
        "Code examples: yes/no",
        "Preferred explanation depth",
        "Tone preference",
        "Auto-synced from wiki facts",
        "Last wiki sync: Not yet synced",
        "needed",
        "unknown"
    ];

    private readonly LeanKernelConfig _config;
    private readonly IAttachmentTextExtractionService? _textExtractor;
    private readonly ILogger<EngagementFileMaintenanceService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EngagementFileMaintenanceService" /> class.
    /// </summary>
    /// <param name="config">The LeanKernel configuration.</param>
    /// <param name="textExtractor">The optional text extractor used for binary documents.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    public EngagementFileMaintenanceService(
        IOptions<LeanKernelConfig> config,
        IAttachmentTextExtractionService? textExtractor,
        ILogger<EngagementFileMaintenanceService> logger)
    {
        _config = config.Value;
        _textExtractor = textExtractor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EngagementFileMaintenanceResult> MaintainAsync(
        EngagementFileMaintenanceRequest request,
        CancellationToken ct)
    {
        var errors = new List<string>();
        var skipped = new List<string>();
        var sourceFilesFound = new List<string>();
        var sourceFilesRead = new List<string>();
        var excerpts = new List<string>();
        var before = await SnapshotTargetsAsync(request.TargetFiles, ct);

        foreach (var documentName in request.SourceDocumentNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var match = FindDocument(documentName);
            if (match is null)
            {
                errors.Add($"Source document not found: {documentName}");
                continue;
            }

            sourceFilesFound.Add(match);
            var text = await ReadSourceTextAsync(match, errors, ct);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            sourceFilesRead.Add(match);
            excerpts.AddRange(CreateSourceExcerpts(match, text));
        }

        var targetPaths = GetTargetPaths(request.TargetFiles).ToList();
        foreach (var targetPath in targetPaths)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                var existing = File.Exists(targetPath)
                    ? await File.ReadAllTextAsync(targetPath, ct)
                    : CreateBaseTemplate(Path.GetFileName(targetPath));
                var cleaned = CleanEngagementContent(Path.GetFileName(targetPath), existing);
                var updated = ApplyVerifiedFacts(Path.GetFileName(targetPath), cleaned, request.UserMessage, excerpts);
                await File.WriteAllTextAsync(targetPath, updated, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(targetPath)}: {ex.Message}");
                _logger.LogWarning(ex, "Failed to maintain engagement file {Path}", targetPath);
            }
        }

        var changed = new List<string>();
        var verified = new List<string>();
        foreach (var targetPath in targetPaths)
        {
            if (!File.Exists(targetPath))
            {
                skipped.Add($"{Path.GetFileName(targetPath)}: file was not created or verified");
                continue;
            }

            verified.Add(targetPath);
            var after = await File.ReadAllTextAsync(targetPath, ct);
            if (!before.TryGetValue(targetPath, out var previous) || !string.Equals(previous, after, StringComparison.Ordinal))
                changed.Add(targetPath);
        }

        return new EngagementFileMaintenanceResult
        {
            Success = errors.Count == 0,
            SourceFilesFound = sourceFilesFound,
            SourceFilesRead = sourceFilesRead,
            ChangedFiles = changed,
            VerifiedFiles = verified,
            SkippedFiles = skipped,
            SourceExcerpts = excerpts,
            Errors = errors
        };
    }

    private async Task<Dictionary<string, string?>> SnapshotTargetsAsync(
        IReadOnlyList<string> targetFiles,
        CancellationToken ct)
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var targetPath in GetTargetPaths(targetFiles))
        {
            snapshot[targetPath] = File.Exists(targetPath)
                ? await File.ReadAllTextAsync(targetPath, ct)
                : null;
        }

        return snapshot;
    }

    private IEnumerable<string> GetTargetPaths(IReadOnlyList<string> requestedTargets)
    {
        var requested = requestedTargets.Count == 0
            ? EngagementFileNames
            : requestedTargets
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => EngagementFileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var agentDir = Path.Combine(_config.Agents.BasePath, "main");
        foreach (var fileName in requested)
            yield return Path.Combine(agentDir, fileName);
    }

    private string? FindDocument(string documentName)
    {
        var dataRoot = ResolveDataRoot();
        if (!Directory.Exists(dataRoot))
            return null;

        var normalizedName = NormalizeFileName(documentName);
        return Directory.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsUnderAgentsDirectory(path))
            .FirstOrDefault(path => string.Equals(
                NormalizeFileName(Path.GetFileName(path)),
                normalizedName,
                StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveDataRoot()
    {
        if (!string.IsNullOrWhiteSpace(_config.Wiki.BasePath))
            return Path.GetFullPath(Path.GetDirectoryName(_config.Wiki.BasePath) ?? _config.Wiki.BasePath);

        return Path.GetFullPath(Path.GetDirectoryName(_config.Agents.BasePath) ?? _config.Agents.BasePath);
    }

    private bool IsUnderAgentsDirectory(string path)
    {
        var agents = Path.GetFullPath(_config.Agents.BasePath);
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(agents, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> ReadSourceTextAsync(string path, List<string> errors, CancellationToken ct)
    {
        var contentType = GuessContentType(path);
        if (RequiresExtraction(path))
        {
            if (_textExtractor is null || !_textExtractor.CanExtractText(contentType, path))
            {
                errors.Add($"Text extraction unavailable for {Path.GetFileName(path)}");
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path, ct);
            var extracted = await _textExtractor.ExtractTextAsync(contentType, path, bytes, ct);
            if (string.IsNullOrWhiteSpace(extracted))
            {
                errors.Add($"No text extracted from {Path.GetFileName(path)}");
                return null;
            }

            return extracted;
        }

        return await File.ReadAllTextAsync(path, ct);
    }

    private static IReadOnlyList<string> CreateSourceExcerpts(string path, string text)
    {
        var fileName = Path.GetFileName(path);
        return text
            .Split(['\r', '\n', '.', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsUsefulExcerpt)
            .Select(value => $"- {fileName}: {Truncate(value, 220)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private static bool IsUsefulExcerpt(string value)
    {
        if (value.Length < 20)
            return false;

        return !PlaceholderMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanEngagementContent(string fileName, string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var result = new List<string>();
        var seenBullets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (IsNoiseLine(fileName, line))
                continue;

            if (line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            {
                var normalized = NormalizeBullet(line);
                if (!seenBullets.Add(normalized))
                    continue;
            }

            result.Add(line);
        }

        return string.Join("\n", result).TrimEnd() + "\n";
    }

    private static bool IsNoiseLine(string fileName, string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        if (PlaceholderMarkers.Any(marker => trimmed.Equals(marker, StringComparison.OrdinalIgnoreCase) ||
                                             trimmed.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (trimmed.Contains("Limitation detected in response", StringComparison.OrdinalIgnoreCase))
            return true;

        if (trimmed.Contains("gap_missing_access: to file system information", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fileName.Equals("SELF.md", StringComparison.OrdinalIgnoreCase) &&
            trimmed.StartsWith("- Name:", StringComparison.OrdinalIgnoreCase) &&
            (trimmed.Contains("resume file name formats", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Contains("user information not included", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Contains("update personal strengths", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (fileName.Equals("USER.md", StringComparison.OrdinalIgnoreCase) &&
            (trimmed.Equals("- Preferred agent name: user information not included in USER.md", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Equals("- Preferred agent name: resume file name formats", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Equals("- Preferred agent name: update personal strengths and competency profile", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Equals("you have access to my resume", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static string ApplyVerifiedFacts(
        string fileName,
        string content,
        string userMessage,
        IReadOnlyList<string> sourceExcerpts)
    {
        return fileName.ToUpperInvariant() switch
        {
            "USER.MD" => ApplyUserFacts(content, userMessage, sourceExcerpts),
            "SELF.MD" => ApplySelfFacts(content, userMessage),
            "AGENTS.MD" => ApplyAgentRules(content, userMessage),
            _ => content
        };
    }

    private static string ApplyUserFacts(string content, string userMessage, IReadOnlyList<string> sourceExcerpts)
    {
        var bullets = new List<string>();
        if (userMessage.Contains("Microsoft Todo", StringComparison.OrdinalIgnoreCase))
            bullets.Add("- Deliverables should be managed in Microsoft Todo, checking completion before resurfacing tasks.");
        if (userMessage.Contains("Doughray", StringComparison.OrdinalIgnoreCase))
            bullets.Add("- Doughray is the preferred skill for financial status checks.");
        if (userMessage.Contains("Sabbath", StringComparison.OrdinalIgnoreCase))
            bullets.Add("- Saturdays are Sabbath boundaries: encourage rest and schedule only religious activities.");
        if (userMessage.Contains("direct", StringComparison.OrdinalIgnoreCase) || userMessage.Contains("pleasantries", StringComparison.OrdinalIgnoreCase))
            bullets.Add("- Communication preference: direct, concise, no pandering, skip pleasantries.");
        if (userMessage.Contains("America/Toronto", StringComparison.OrdinalIgnoreCase))
            bullets.Add("- Timezone: America/Toronto.");
        if (userMessage.Contains("passive income", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("role/opportunity", StringComparison.OrdinalIgnoreCase))
            bullets.Add("- Current priorities: find a challenging strengths-aligned role, identify passive-income ventures, and save/invest toward a business venture.");

        if (sourceExcerpts.Count > 0)
        {
            content = AppendUniqueToSection(content, "Source-Backed Document Insights", sourceExcerpts);
        }

        return bullets.Count == 0
            ? content
            : AppendUniqueToSection(content, "Verified Preferences and Priorities", bullets);
    }

    private static string ApplySelfFacts(string content, string userMessage)
    {
        var bullets = new List<string>();
        if (userMessage.Contains("Agent name: Kem", StringComparison.OrdinalIgnoreCase))
            bullets.Add("- Preferred agent name: Kem.");
        if (userMessage.Contains("proactive", StringComparison.OrdinalIgnoreCase) || userMessage.Contains("blind spots", StringComparison.OrdinalIgnoreCase))
            bullets.Add("- Operating model: proactive, explores options, and covers blind spots.");

        bullets.Add("- Verify source reads and durable file changes before claiming engagement files were updated.");
        bullets.Add("- Do not store task requests, placeholder text, or generic failure narratives as user profile facts.");

        return AppendUniqueToSection(content, "Operating Rules", bullets);
    }

    private static string ApplyAgentRules(string content, string userMessage)
    {
        var bullets = new List<string>
        {
            "- Verify source reads and post-write file state before claiming file update completion.",
            "- For engagement-file maintenance, report source files read, files changed, files unchanged, and explicit errors."
        };

        if (userMessage.Contains("direct", StringComparison.OrdinalIgnoreCase) || userMessage.Contains("pleasantries", StringComparison.OrdinalIgnoreCase))
            content = UpdateSection(content, "Agent Personality", "**Tone:** direct, concise, no pandering");
        if (userMessage.Contains("America/Toronto", StringComparison.OrdinalIgnoreCase))
            content = UpdateSection(content, "Time Boundaries", "**Timezone:** America/Toronto");

        return AppendUniqueToSection(content, "Useful By Default", bullets);
    }

    private static string AppendUniqueToSection(string content, string sectionName, IEnumerable<string> values)
    {
        var updated = content;
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            if (updated.Contains(value, StringComparison.OrdinalIgnoreCase))
                continue;

            updated = AppendToSection(updated, sectionName, value);
        }

        return updated;
    }

    private static string AppendToSection(string content, string sectionName, string value)
    {
        var sectionPattern = new Regex($@"(## {Regex.Escape(sectionName)}.*?)(?=## |$)", RegexOptions.Singleline);
        var match = sectionPattern.Match(content);
        if (match.Success)
        {
            var replacement = match.Groups[1].Value.TrimEnd() + $"\n\n{value}\n";
            return sectionPattern.Replace(content, replacement, 1);
        }

        return content.TrimEnd() + $"\n\n## {sectionName}\n\n{value}\n";
    }

    private static string UpdateSection(string content, string sectionName, string value)
    {
        var sectionPattern = new Regex($@"## {Regex.Escape(sectionName)}.*?(?=## |$)", RegexOptions.Singleline);
        return sectionPattern.IsMatch(content)
            ? sectionPattern.Replace(content, $"## {sectionName}\n\n{value}\n\n", 1)
            : content.TrimEnd() + $"\n\n## {sectionName}\n\n{value}\n";
    }

    private static string CreateBaseTemplate(string fileName)
    {
        return fileName.ToUpperInvariant() switch
        {
            "USER.MD" => "# USER.md - User Profile & Preferences\n\n## Verified Preferences and Priorities\n\nNo verified facts yet.\n",
            "SELF.MD" => "# SELF.md - Agent Self-Definition\n\n## Operating Rules\n\nNo verified facts yet.\n",
            "AGENTS.MD" => "# AGENTS.md - Rules of Engagement\n\n## Agent Personality\n\n**Tone:** direct, concise\n\n## Scope of Autonomy\n\n### Can Do Without Asking\n\n- ReadFile\n- ListFiles\n- SearchFiles\n- StatFile\n- SearchKnowledge\n- SearchWiki\n- WriteAgentsMd\n- WriteSelfMd\n- WriteUserMd\n\n### Must Ask Before\n\n- WriteFile\n- SendMessage\n- DeleteFile\n\n### Never Do\n\n- CommitSecrets\n- DeleteProductionData\n- ExposeSecret\n\n## Useful By Default\n\n- Verify source reads and post-write file state before claiming file update completion.\n",
            _ => ""
        };
    }

    private static string NormalizeFileName(string value)
        => value.Trim().Trim('`', '"', '\'').Normalize(NormalizationForm.FormC);

    private static string NormalizeBullet(string value)
        => Regex.Replace(value.Trim().TrimStart('-').Trim(), @"\s+", " ");

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "...";

    private static string? GuessContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            _ => null
        };
    }

    private static bool RequiresExtraction(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".pdf" or ".doc" or ".docx";
    }
}
