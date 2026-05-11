using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Enhancement;

/// <summary>
/// Replaces engagement-file update claims with deterministic, verified maintenance results.
/// </summary>
public sealed class EngagementFileMaintenanceResponseEnhancer : IResponseEnhancer
{
    private static readonly string[] EngagementFiles = ["AGENTS.md", "SELF.md", "USER.md"];
    private readonly IEngagementFileMaintenanceService _maintenanceService;
    private readonly ILogger<EngagementFileMaintenanceResponseEnhancer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EngagementFileMaintenanceResponseEnhancer" /> class.
    /// </summary>
    /// <param name="maintenanceService">The deterministic engagement maintenance service.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    public EngagementFileMaintenanceResponseEnhancer(
        IEngagementFileMaintenanceService maintenanceService,
        ILogger<EngagementFileMaintenanceResponseEnhancer> logger)
    {
        _maintenanceService = maintenanceService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> EnhanceResponseAsync(
        string userQuery,
        string assistantResponse,
        ConversationContext context,
        CancellationToken ct)
    {
        if (!IsEngagementMaintenanceRequest(userQuery))
            return assistantResponse;

        _logger.LogInformation("Running deterministic engagement file maintenance.");
        var request = new EngagementFileMaintenanceRequest(
            userQuery,
            ExtractDocumentNames(userQuery),
            ExtractTargetFiles(userQuery));

        var result = await _maintenanceService.MaintainAsync(request, ct);
        return FormatResult(result);
    }

    private static bool IsEngagementMaintenanceRequest(string userQuery)
    {
        var mentionsEngagementTarget =
            userQuery.Contains("engagement files", StringComparison.OrdinalIgnoreCase) ||
            EngagementFiles.Any(file => userQuery.Contains(file, StringComparison.OrdinalIgnoreCase));

        var requestsMaintenance = Regex.IsMatch(
            userQuery,
            @"\b(read|find|update|refresh|rewrite|clean|configure|use\s+the\s+insights)\b",
            RegexOptions.IgnoreCase);

        return mentionsEngagementTarget && requestsMaintenance;
    }

    private static IReadOnlyList<string> ExtractDocumentNames(string userQuery)
    {
        var quotedMatches = Regex.Matches(
            userQuery,
            @"[`""](?<name>[^`""\r\n]+?\.(?:pdf|docx|doc|md|txt))[`""]",
            RegexOptions.IgnoreCase);
        var tokenMatches = Regex.Matches(
            userQuery,
            @"(?<![\w./-])(?<name>[\w./()_-]+?\.(?:pdf|docx|doc|md|txt))(?![\w./-])",
            RegexOptions.IgnoreCase);

        return quotedMatches
            .Concat(tokenMatches)
            .Select(match => match.Groups["name"].Value.Trim().Trim('-', '*', ' ', '`', '"'))
            .Where(value => !string.IsNullOrWhiteSpace(value) && !EngagementFiles.Contains(value, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractTargetFiles(string userQuery)
    {
        return EngagementFiles
            .Where(file => userQuery.Contains(file, StringComparison.OrdinalIgnoreCase))
            .DefaultIfEmpty()
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Cast<string>()
            .ToArray();
    }

    private static string FormatResult(EngagementFileMaintenanceResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(result.Success
            ? "Engagement file maintenance completed with verified state."
            : "Engagement file maintenance completed with errors.");

        AppendList(builder, "Source files found", result.SourceFilesFound);
        AppendList(builder, "Source files read", result.SourceFilesRead);
        AppendList(builder, "Engagement files changed", result.ChangedFiles);
        AppendList(builder, "Engagement files verified", result.VerifiedFiles);
        AppendList(builder, "Skipped", result.SkippedFiles);
        AppendList(builder, "Errors", result.Errors);

        if (result.SourceExcerpts.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("**Source-backed excerpts used:**");
            foreach (var excerpt in result.SourceExcerpts.Take(8))
                builder.AppendLine(excerpt);
        }

        if (!result.HasChanges && result.Errors.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("No engagement file content changed after cleanup and verification.");
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendList(StringBuilder builder, string title, IReadOnlyList<string> values)
    {
        builder.AppendLine();
        builder.AppendLine($"**{title}:**");
        if (values.Count == 0)
        {
            builder.AppendLine("- none");
            return;
        }

        foreach (var value in values)
            builder.AppendLine($"- {value}");
    }
}
