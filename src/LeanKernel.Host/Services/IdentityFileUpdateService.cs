using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Host.Services;

namespace LeanKernel.Host.Services;

/// <summary>
/// Continuously updates identity files (USER.md, SELF.md, AGENTS.md) from conversation insights.
/// Called at the end of each turn to capture and record learning from the exchange.
/// 
/// Unlike LlmWikiExtractor which extracts to wiki, this service extracts directly to
/// identity file sections, enabling dynamic agent self-improvement without explicit prompting.
/// </summary>
public sealed class IdentityFileUpdateService : IIdentityFileUpdateService
{
    private readonly LeanKernelHostPaths _paths;
    private readonly IWikiStore _wiki;
    private readonly ILogger<IdentityFileUpdateService> _logger;

    public IdentityFileUpdateService(
        LeanKernelHostPaths paths,
        IWikiStore wiki,
        ILogger<IdentityFileUpdateService> logger)
    {
        _paths = paths;
        _wiki = wiki;
        _logger = logger;
    }

    /// <summary>
    /// Extract insights from a conversation turn and update identity files.
    /// This is called at the end of ProcessAsync to enable continuous learning.
    /// </summary>
    public async Task UpdateFromTurnAsync(
        string userMessage,
        string assistantResponse,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            // Extract user insights
            var userInsights = ExtractUserInsights(userMessage);
            if (userInsights.Count > 0)
            {
                await UpdateUserProfileAsync(userInsights, ct);
            }

            // Extract agent insights
            var agentInsights = ExtractAgentInsights(assistantResponse);
            if (agentInsights.Count > 0)
            {
                await UpdateAgentProfileAsync(agentInsights, ct);
            }

            // Extract capability needs (e.g., "I don't have a tool for...")
            var capabilityGaps = ExtractCapabilityGaps(assistantResponse);
            if (capabilityGaps.Count > 0)
            {
                await UpdateCapabilityGapsAsync(capabilityGaps, ct);
            }

            _logger.LogDebug(
                "Identity files updated: {UserInsights} user, {AgentInsights} agent, {CapGaps} capability gap insights",
                userInsights.Count, agentInsights.Count, capabilityGaps.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Identity file update failed for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Extract insights about the user from their message.
    /// </summary>
    private static Dictionary<string, string> ExtractUserInsights(string userMessage)
    {
        var insights = new Dictionary<string, string>();

        // Pattern 1: Role/title mentions ("I'm a manager at...", "As a developer...")
        var roleMatch = Regex.Match(userMessage, 
            @"(?:i'?m|as|being|working as)\s+(?:a\s+)?(\w+(?:\s+\w+)?)", 
            RegexOptions.IgnoreCase);
        if (roleMatch.Success)
        {
            insights["role"] = roleMatch.Groups[1].Value;
        }

        // Pattern 2: Company/organization mentions
        var companyMatch = Regex.Match(userMessage,
            @"(?:at|for|work|company|organization)\s+([A-Z][A-Za-z\s&]+)",
            RegexOptions.IgnoreCase);
        if (companyMatch.Success)
        {
            insights["organization"] = companyMatch.Groups[1].Value.Trim();
        }

        // Pattern 3: Domain expertise
        if (userMessage.Contains("expert", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("specialized", StringComparison.OrdinalIgnoreCase))
        {
            // Extract what they claim expertise in
            var contextWindow = ExtractContextWindow(userMessage, 100);
            insights["expertise"] = contextWindow;
        }

        return insights;
    }

    /// <summary>
    /// Extract insights about the agent from its response.
    /// </summary>
    private static Dictionary<string, string> ExtractAgentInsights(string assistantResponse)
    {
        var insights = new Dictionary<string, string>();

        // Pattern: Response quality indicators
        if (assistantResponse.Contains("based on your", StringComparison.OrdinalIgnoreCase))
        {
            insights["personalization_level"] = "high";
        }

        if (assistantResponse.Contains("I don't", StringComparison.OrdinalIgnoreCase) ||
            assistantResponse.Contains("I cannot", StringComparison.OrdinalIgnoreCase))
        {
            insights["limitation_detected"] = "true";
        }

        // Pattern: Tool/skill used
        if (assistantResponse.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            insights["tool_used"] = "search_knowledge";
        }

        return insights;
    }

    /// <summary>
    /// Extract capability gaps from the response.
    /// </summary>
    private static Dictionary<string, string> ExtractCapabilityGaps(string assistantResponse)
    {
        var gaps = new Dictionary<string, string>();

        // Patterns indicating missing capabilities
        var patterns = new[]
        {
            (@"(?:i (?:don't have|don't|cannot|can't) (?:access|support|handle|process)\s+(.+?)\.)", "missing_access"),
            (@"(?:i (?:would need|would require|would benefit from)\s+(?:a|the\s+)?(.+?)(?:\sto|\.|,))", "needed_capability"),
            (@"(?:(?:unable|cannot) to (?:find|locate|access)\s+(.+?)\.)", "search_limitation"),
        };

        foreach (var (pattern, gapType) in patterns)
        {
            var match = Regex.Match(assistantResponse, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                gaps[$"gap_{gapType}"] = match.Groups[1].Value.Trim();
            }
        }

        return gaps;
    }

    /// <summary>
    /// Update USER.md with new user insights.
    /// </summary>
    private async Task UpdateUserProfileAsync(Dictionary<string, string> insights, CancellationToken ct)
    {
        try
        {
            var userPath = Path.Combine(_paths.AgentsDirectory, "main", "USER.md");
            if (!File.Exists(userPath))
            {
                _logger.LogDebug("USER.md not found at {Path}, skipping update", userPath);
                return;
            }

            var content = await File.ReadAllTextAsync(userPath, ct);

            // Update role if present
            if (insights.TryGetValue("role", out var role))
            {
                content = UpdateSection(content, "Role", role);
            }

            // Update organization
            if (insights.TryGetValue("organization", out var org))
            {
                content = UpdateSection(content, "Organization", org);
            }

            // Append expertise
            if (insights.TryGetValue("expertise", out var expertise))
            {
                content = AppendToSection(content, "Expertise", expertise);
            }

            await File.WriteAllTextAsync(userPath, content, ct);
            _logger.LogDebug("Updated USER.md with new insights");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update USER.md");
        }
    }

    /// <summary>
    /// Update SELF.md with agent insights about its own performance.
    /// </summary>
    private async Task UpdateAgentProfileAsync(Dictionary<string, string> insights, CancellationToken ct)
    {
        try
        {
            var selfPath = Path.Combine(_paths.AgentsDirectory, "main", "SELF.md");
            if (!File.Exists(selfPath))
            {
                _logger.LogDebug("SELF.md not found at {Path}, skipping update", selfPath);
                return;
            }

            var content = await File.ReadAllTextAsync(selfPath, ct);

            // Track personalization level
            if (insights.TryGetValue("personalization_level", out var personalization))
            {
                content = UpdateSection(content, "Personalization", personalization);
            }

            // Log limitations detected
            if (insights.TryGetValue("limitation_detected", out _))
            {
                content = AppendToSection(content, "Limitations Observed", 
                    $"- {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}: Limitation detected in response");
            }

            await File.WriteAllTextAsync(selfPath, content, ct);
            _logger.LogDebug("Updated SELF.md with new insights");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update SELF.md");
        }
    }

    /// <summary>
    /// Track capability gaps for future tool creation or skill enhancement.
    /// </summary>
    private async Task UpdateCapabilityGapsAsync(Dictionary<string, string> gaps, CancellationToken ct)
    {
        try
        {
            var selfPath = Path.Combine(_paths.AgentsDirectory, "main", "SELF.md");
            if (!File.Exists(selfPath))
                return;

            var content = await File.ReadAllTextAsync(selfPath, ct);

            foreach (var (gapType, description) in gaps)
            {
                content = AppendToSection(content, "Capability Gaps", 
                    $"- {gapType}: {description}");
            }

            await File.WriteAllTextAsync(selfPath, content, ct);
            _logger.LogInformation("Tracked {Count} capability gaps", gaps.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update capability gaps");
        }
    }

    /// <summary>
    /// Update a section in the markdown file with new content.
    /// </summary>
    private static string UpdateSection(string content, string sectionName, string value)
    {
        // Find section header
        var sectionPattern = new Regex($@"## {Regex.Escape(sectionName)}.*?(?=##|$)", RegexOptions.Singleline);
        var match = sectionPattern.Match(content);

        if (match.Success)
        {
            var replacement = $"## {sectionName}\n\n{value}\n";
            return sectionPattern.Replace(content, replacement, 1);
        }

        return content;
    }

    /// <summary>
    /// Append content to the end of a section without replacing existing content.
    /// </summary>
    private static string AppendToSection(string content, string sectionName, string value)
    {
        var sectionPattern = new Regex($@"(## {Regex.Escape(sectionName)}.*?)(?=## |$)", RegexOptions.Singleline);
        var match = sectionPattern.Match(content);

        if (match.Success)
        {
            var replacement = match.Groups[1].Value.TrimEnd() + $"\n\n{value}\n";
            return sectionPattern.Replace(content, replacement, 1);
        }

        // Section doesn't exist, add it at the end
        return content.TrimEnd() + $"\n\n## {sectionName}\n\n{value}\n";
    }

    /// <summary>
    /// Extract a window of context around a pattern for analysis.
    /// </summary>
    private static string ExtractContextWindow(string text, int windowSize)
    {
        return text.Length > windowSize 
            ? text.Substring(0, windowSize) + "..." 
            : text;
    }
}
