using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Identity;

/// <summary>
/// Continuously updates identity files (USER.md, SELF.md, AGENTS.md) from conversation insights.
/// Called at the end of each turn to capture and record learning from the exchange.
/// 
/// Unlike LlmWikiExtractor which extracts to wiki, this service extracts directly to
/// identity file sections, enabling dynamic agent self-improvement without explicit prompting.
/// </summary>
public sealed class IdentityFileUpdateService : IIdentityFileUpdateService
{
    private readonly LeanKernelConfig _config;
    private readonly IAgentSelfProfileInitializer? _selfProfileInitializer;
    private readonly IUserProfileSynchronizer? _userProfileSynchronizer;
    private readonly IActionAuthorizer? _actionAuthorizer;
    private readonly ILogger<IdentityFileUpdateService> _logger;
    private readonly IReadOnlyList<IOnboardingStep> _onboardingSteps;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityFileUpdateService" /> class.
    /// </summary>
    /// <param name="config">The LeanKernel configuration containing the agent path.</param>
    /// <param name="wiki">The wiki store available for identity-related lookup.</param>
    /// <param name="logger">The logger used for identity update diagnostics.</param>
    public IdentityFileUpdateService(
        IOptions<LeanKernelConfig> config,
        IWikiStore wiki,
        IAgentSelfProfileInitializer? selfProfileInitializer,
        IUserProfileSynchronizer? userProfileSynchronizer,
        IActionAuthorizer? actionAuthorizer,
        ILogger<IdentityFileUpdateService> logger,
        IEnumerable<IOnboardingStep>? onboardingSteps = null)
    {
        _config = config.Value;
        _ = wiki;
        _selfProfileInitializer = selfProfileInitializer;
        _userProfileSynchronizer = userProfileSynchronizer;
        _actionAuthorizer = actionAuthorizer;
        _logger = logger;
        _onboardingSteps = onboardingSteps?.ToList() ?? [];
    }

    /// <summary>
    /// Extract insights from a conversation turn and update identity files.
    /// This is called at the end of ProcessAsync to enable continuous learning.
    /// </summary>
    public async Task<IdentityFileUpdateResult> UpdateFromTurnAsync(
        string userMessage,
        string assistantResponse,
        string sessionId,
        CancellationToken ct)
    {
        var before = await SnapshotIdentityFilesAsync(ct);
        var errors = new List<string>();
        try
        {
            await EnsureIdentityFilesExistAsync(ct);

            // If the user explicitly points out a miss, codify it as an operational correction.
            // This keeps the agent useful-by-default without requiring explicit file-update instructions.
            await ApplyCorrectiveFeedbackAsync(userMessage, ct);

            // Extract user insights
            var userInsights = ExtractUserInsights(userMessage);
            if (userInsights.Count > 0)
            {
                await UpdateUserProfileAsync(userInsights, errors, ct);

                var agentProfileInsights = userInsights
                    .Where(kv => kv.Key is "agent_name" or "engagement_model" or "autonomy")
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                if (agentProfileInsights.Count > 0)
                {
                    await UpdateAgentProfileAsync(agentProfileInsights, errors, ct);
                }
            }

            // Extract agent insights
            var agentInsights = ExtractAgentInsights(assistantResponse);
            if (agentInsights.Count > 0)
            {
                await UpdateAgentProfileAsync(agentInsights, errors, ct);
            }

            // Extract capability needs (e.g., "I don't have a tool for...")
            var capabilityGaps = ExtractCapabilityGaps(assistantResponse);
            if (capabilityGaps.Count > 0)
            {
                await UpdateCapabilityGapsAsync(capabilityGaps, errors, ct);
            }

            _logger.LogDebug(
                "Identity files updated: {UserInsights} user, {AgentInsights} agent, {CapGaps} capability gap insights",
                userInsights.Count, agentInsights.Count, capabilityGaps.Count);

            return await BuildUpdateResultAsync(before, errors, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Identity file update failed for session {SessionId}", sessionId);
            var partialResult = await BuildUpdateResultAsync(before, errors, ct);
            return partialResult with
            {
                Success = false,
                Errors = partialResult.Errors.Concat([ex.Message]).ToArray()
            };
        }
    }

    private async Task<Dictionary<string, string?>> SnapshotIdentityFilesAsync(CancellationToken ct)
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in GetIdentityFilePaths())
        {
            snapshot[path] = File.Exists(path)
                ? await File.ReadAllTextAsync(path, ct)
                : null;
        }

        return snapshot;
    }

    private async Task<IdentityFileUpdateResult> BuildUpdateResultAsync(
        IReadOnlyDictionary<string, string?> before,
        IReadOnlyList<string>? priorErrors,
        CancellationToken ct)
    {
        var changed = new List<string>();
        var verified = new List<string>();
        var errors = priorErrors is { Count: > 0 } ? priorErrors.ToList() : [];

        foreach (var path in GetIdentityFilePaths())
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                verified.Add(path);
                var after = await File.ReadAllTextAsync(path, ct);
                if (!before.TryGetValue(path, out var previous) || !string.Equals(previous, after, StringComparison.Ordinal))
                    changed.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return new IdentityFileUpdateResult
        {
            Success = errors.Count == 0,
            ChangedFiles = changed,
            VerifiedFiles = verified,
            Errors = errors
        };
    }

    private IEnumerable<string> GetIdentityFilePaths()
    {
        var agentDir = Path.Combine(_config.Agents.BasePath, "main");
        yield return Path.Combine(agentDir, "AGENTS.md");
        yield return Path.Combine(agentDir, "SELF.md");
        yield return Path.Combine(agentDir, "USER.md");
    }

    private async Task EnsureIdentityFilesExistAsync(CancellationToken ct)
    {
        try
        {
            if (await CanWriteAsync("WriteAgentsMd", ct))
            {
                var agentsStep = _onboardingSteps.FirstOrDefault(s =>
                    string.Equals(s.Name, "agents", StringComparison.OrdinalIgnoreCase));
                if (agentsStep is not null)
                {
                    var agentsResult = await agentsStep.InitializeAsync(ct);
                    if (!agentsResult.Success)
                    {
                        _logger.LogWarning("Failed to initialize AGENTS.md during identity refresh: {Message}", agentsResult.Message);
                    }
                }
                else
                {
                    await EnsureAgentsFileFallbackAsync(ct);
                }
            }

            if (await CanWriteAsync("WriteSelfMd", ct) && _selfProfileInitializer is not null)
            {
                var selfResult = await _selfProfileInitializer.InitializeAsync(ct);
                if (!selfResult.Success)
                {
                    _logger.LogWarning("Failed to initialize SELF.md during identity refresh: {Message}", selfResult.Message);
                }
            }

            if (await CanWriteAsync("WriteUserMd", ct) && _userProfileSynchronizer is not null)
            {
                var userResult = await _userProfileSynchronizer.InitializeAsync(ct);
                if (!userResult.Success)
                {
                    _logger.LogWarning("Failed to initialize USER.md during identity refresh: {Message}", userResult.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Identity file self-healing failed.");
        }
    }

    private async Task EnsureAgentsFileFallbackAsync(CancellationToken ct)
    {
        var agentsPath = Path.Combine(_config.Agents.BasePath, "main", "AGENTS.md");
        if (File.Exists(agentsPath))
            return;

        var agentsDir = Path.GetDirectoryName(agentsPath);
        if (agentsDir is not null)
            Directory.CreateDirectory(agentsDir);

        await File.WriteAllTextAsync(agentsPath, GenerateFallbackAgentsTemplate(), ct);
    }

    private async Task<bool> CanWriteAsync(string actionType, CancellationToken ct)
    {
        if (_actionAuthorizer is null)
            return true;

        var result = await _actionAuthorizer.AuthorizeAsync(actionType, ct);
        if (result.IsAuthorized)
            return true;

        _logger.LogDebug("Skipped {ActionType}: {Reason}", actionType, result.Reason ?? "not authorized");
        return false;
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

        var agentNameMatch = Regex.Match(userMessage, @"Agent name:\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase);
        if (agentNameMatch.Success)
        {
            insights["agent_name"] = agentNameMatch.Groups["value"].Value.Trim();
        }

        var engagementMatch = Regex.Match(userMessage, @"Engagement model:\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase);
        if (engagementMatch.Success)
        {
            insights["engagement_model"] = engagementMatch.Groups["value"].Value.Trim();
        }

        var communicationsMatch = Regex.Match(userMessage, @"Communications?:\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase);
        if (communicationsMatch.Success)
        {
            insights["communication_preferences"] = communicationsMatch.Groups["value"].Value.Trim();
        }

        var autonomyMatch = Regex.Match(userMessage, @"Autonomy:\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase);
        if (autonomyMatch.Success)
        {
            insights["autonomy"] = autonomyMatch.Groups["value"].Value.Trim();
        }

        var timezoneMatch = Regex.Match(userMessage, @"Timezone:\s*(?<value>[^\r\n,]+)", RegexOptions.IgnoreCase);
        if (timezoneMatch.Success)
        {
            insights["timezone"] = timezoneMatch.Groups["value"].Value.Trim();
        }

        if (userMessage.Contains("Availability:", StringComparison.OrdinalIgnoreCase))
        {
            insights["availability"] = ExtractContextWindow(userMessage, 500);
        }

        if (userMessage.Contains("Sabbath", StringComparison.OrdinalIgnoreCase))
        {
            insights["sabbath"] = ExtractContextWindow(userMessage, 500);
        }

        if (userMessage.Contains("Top Priorities", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("Find a role/opportunity", StringComparison.OrdinalIgnoreCase))
        {
            insights["priorities"] = ExtractContextWindow(userMessage, 700);
        }

        if (userMessage.Contains("Microsoft Todo", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("Doughray", StringComparison.OrdinalIgnoreCase) ||
            userMessage.Contains("Career", StringComparison.OrdinalIgnoreCase))
        {
            insights["tools_and_integrations"] = ExtractContextWindow(userMessage, 700);
        }

        return insights;
    }

    private static string? ExtractCorrectiveFeedback(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        var normalized = userMessage.Trim();
        var patterns = new[]
        {
            @"\byou\s+(?:still\s+)?(?:haven't|didn't|did not|failed to|never)\b",
            @"\bit looks like you\b",
            @"\blooks like you\b",
            @"\bmissed\b",
            @"\bnot done\b"
        };

        return patterns.Any(p => Regex.IsMatch(normalized, p, RegexOptions.IgnoreCase))
            ? normalized
            : null;
    }

    private async Task ApplyCorrectiveFeedbackAsync(string userMessage, CancellationToken ct)
    {
        var correctiveFeedback = ExtractCorrectiveFeedback(userMessage);
        if (correctiveFeedback is null)
            return;

        var correctionBullet = $"- {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC: User flagged a missed action. Verify state first, then execute correction without waiting for explicit file-update instructions. Trigger: \"{correctiveFeedback}\"";

        if (await CanWriteAsync("WriteSelfMd", ct))
        {
            var selfPath = Path.Combine(_config.Agents.BasePath, "main", "SELF.md");
            if (File.Exists(selfPath))
            {
                var selfContent = await File.ReadAllTextAsync(selfPath, ct);
                selfContent = AppendToSection(selfContent, "Correction Protocol", correctionBullet);
                await File.WriteAllTextAsync(selfPath, selfContent, ct);
            }
        }

        if (await CanWriteAsync("WriteAgentsMd", ct))
        {
            var agentsPath = Path.Combine(_config.Agents.BasePath, "main", "AGENTS.md");
            if (File.Exists(agentsPath))
            {
                var agentsContent = await File.ReadAllTextAsync(agentsPath, ct);
                agentsContent = AppendToSection(agentsContent, "Useful By Default", correctionBullet);
                await File.WriteAllTextAsync(agentsPath, agentsContent, ct);
            }
        }

        if (await CanWriteAsync("WriteUserMd", ct))
        {
            var userPath = Path.Combine(_config.Agents.BasePath, "main", "USER.md");
            if (File.Exists(userPath))
            {
                var userContent = await File.ReadAllTextAsync(userPath, ct);
                userContent = AppendToSection(
                    userContent,
                    "Agent Operation Preferences",
                    "- When I point out a missed action, self-correct and update engagement files by default unless I explicitly require permission-first behavior.");
                await File.WriteAllTextAsync(userPath, userContent, ct);
            }
        }
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
    private async Task UpdateUserProfileAsync(
        Dictionary<string, string> insights,
        List<string> errors,
        CancellationToken ct)
    {
        try
        {
            var userPath = Path.Combine(_config.Agents.BasePath, "main", "USER.md");
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

            if (insights.TryGetValue("agent_name", out var agentName))
            {
                content = AppendUniqueToSection(content, "Agent Operation Preferences", $"- Preferred agent name: {agentName}");
            }

            if (insights.TryGetValue("engagement_model", out var engagementModel))
            {
                content = AppendUniqueToSection(content, "Agent Operation Preferences", $"- Engagement model: {engagementModel}");
            }

            if (insights.TryGetValue("autonomy", out var autonomy))
            {
                content = AppendUniqueToSection(content, "Agent Operation Preferences", $"- Autonomy: {autonomy}");
            }

            if (insights.TryGetValue("communication_preferences", out var communicationPreferences))
            {
                content = AppendUniqueToSection(content, "Communication Preferences", $"- {communicationPreferences}");
            }

            if (insights.TryGetValue("timezone", out var timezone))
            {
                content = AppendUniqueToSection(content, "Availability and Time Boundaries", $"- Timezone: {timezone}");
            }

            if (insights.TryGetValue("availability", out var availability))
            {
                content = AppendUniqueToSection(content, "Availability and Time Boundaries", $"- Availability: {availability}");
            }

            if (insights.TryGetValue("sabbath", out var sabbath))
            {
                content = AppendUniqueToSection(content, "Availability and Time Boundaries", $"- Sabbath boundary: {sabbath}");
            }

            if (insights.TryGetValue("priorities", out var priorities))
            {
                content = AppendUniqueToSection(content, "Priorities", $"- {priorities}");
            }

            if (insights.TryGetValue("tools_and_integrations", out var tools))
            {
                content = AppendUniqueToSection(content, "Tools and Integrations", $"- {tools}");
            }

            await File.WriteAllTextAsync(userPath, content, ct);
            _logger.LogDebug("Updated USER.md with new insights");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update USER.md");
            errors.Add($"USER.md: {ex.Message}");
        }
    }

    /// <summary>
    /// Update SELF.md with agent insights about its own performance.
    /// </summary>
    private async Task UpdateAgentProfileAsync(
        Dictionary<string, string> insights,
        List<string> errors,
        CancellationToken ct)
    {
        try
        {
            var selfPath = Path.Combine(_config.Agents.BasePath, "main", "SELF.md");
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

            if (insights.TryGetValue("agent_name", out var agentName))
            {
                content = UpdateSection(content, "Agent Name", agentName);
                content = AppendUniqueToSection(content, "Agent Identity", $"- Name: {agentName}");
            }

            if (insights.TryGetValue("engagement_model", out var engagementModel))
            {
                content = AppendUniqueToSection(content, "Operating Model", $"- {engagementModel}");
            }

            if (insights.TryGetValue("autonomy", out var autonomy))
            {
                content = AppendUniqueToSection(content, "Operating Model", $"- Autonomy: {autonomy}");
            }

            await File.WriteAllTextAsync(selfPath, content, ct);
            _logger.LogDebug("Updated SELF.md with new insights");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update SELF.md");
            errors.Add($"SELF.md: {ex.Message}");
        }
    }

    /// <summary>
    /// Track capability gaps for future tool creation or skill enhancement.
    /// </summary>
    private async Task UpdateCapabilityGapsAsync(
        Dictionary<string, string> gaps,
        List<string> errors,
        CancellationToken ct)
    {
        try
        {
            var selfPath = Path.Combine(_config.Agents.BasePath, "main", "SELF.md");
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
            errors.Add($"SELF.md capability gaps: {ex.Message}");
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

        return content.TrimEnd() + $"\n\n## {sectionName}\n\n{value}\n";
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

    private static string AppendUniqueToSection(string content, string sectionName, string value)
    {
        if (content.Contains(value, StringComparison.OrdinalIgnoreCase))
            return content;

        return AppendToSection(content, sectionName, value);
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

    private static string GenerateFallbackAgentsTemplate()
    {
        return """
        ---
        version: 2
        ---

        # AGENTS.md - Rules of Engagement

        ## Agent Personality

        **Tone:** direct, concise

        ## Scope of Autonomy

        ### Can Do Without Asking

        - ReadFile
        - ListFiles
        - SearchFiles
        - StatFile
        - SearchKnowledge
        - SearchWiki
        - WriteAgentsMd
        - WriteSelfMd
        - WriteUserMd

        ### Must Ask Before

        - WriteFile
        - CreateDirectory
        - MoveFile
        - CopyFile
        - ChangeFilePermissions
        - DeleteFile
        - SendEmail
        - SendMessage
        - PushCode
        - ModifyConfig

        ### Never Do

        - CommitSecrets
        - DeleteProductionData
        - ExposeSecret

        ## Time Boundaries

        No verified time boundaries yet.

        ## Useful By Default

        - Verify state before claiming file creation or updates.
        - Self-correct missed engagement-file actions without waiting for another prompt when policy allows.
        """;
    }
}
