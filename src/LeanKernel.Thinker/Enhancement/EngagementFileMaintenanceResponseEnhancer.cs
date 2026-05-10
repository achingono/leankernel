using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using System.Text.RegularExpressions;

namespace LeanKernel.Thinker.Enhancement;

/// <summary>
/// Ensures requested engagement-file maintenance is executed and verified before responding.
/// </summary>
public sealed class EngagementFileMaintenanceResponseEnhancer : IResponseEnhancer
{
    private readonly IIdentityFileUpdateService _identityFileUpdateService;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<EngagementFileMaintenanceResponseEnhancer> _logger;
    private readonly IEngagementIntentClassifier? _intentClassifier;

    /// <summary>
    /// Initializes a new instance of the <see cref="EngagementFileMaintenanceResponseEnhancer" /> class.
    /// </summary>
    public EngagementFileMaintenanceResponseEnhancer(
        IIdentityFileUpdateService identityFileUpdateService,
        IOptions<LeanKernelConfig> config,
        ILogger<EngagementFileMaintenanceResponseEnhancer> logger,
        IEngagementIntentClassifier? intentClassifier = null)
    {
        _identityFileUpdateService = identityFileUpdateService;
        _config = config.Value;
        _logger = logger;
        _intentClassifier = intentClassifier;
    }

    /// <inheritdoc />
    public async Task<string> EnhanceResponseAsync(
        string userQuery,
        string assistantResponse,
        ConversationContext context,
        CancellationToken ct)
    {
        var ruleBasedMatch = IsEngagementFileMaintenanceRequest(userQuery, assistantResponse);
        var classification = _intentClassifier is null
            ? EngagementIntentClassification.NoUpdate("No classifier configured.")
            : await _intentClassifier.ClassifyAsync(userQuery, ct);

        if (!ruleBasedMatch && !classification.ShouldUpdate)
            return assistantResponse;

        try
        {
            await ReplayRecentContextAsync(context, ct);
            await _identityFileUpdateService.UpdateFromTurnAsync(
                BuildIdentityUpdateMessage(userQuery, classification),
                assistantResponse,
                "engagement-file-maintenance",
                ct);

            var verification = VerifyEngagementFiles();
            if (verification.AllPresent)
            {
                return $"""
                    Engagement files verified and updated:
                    - AGENTS.md: {verification.AgentsPath}
                    - SELF.md: {verification.SelfPath}
                    - USER.md: {verification.UserPath}

                    {assistantResponse}
                    """;
            }

            return $"""
                Engagement file maintenance ran, but verification is incomplete: {verification.MissingSummary}

                {assistantResponse}
                """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Engagement file maintenance failed.");
            return $"""
                Engagement file maintenance failed: {ex.Message}

                {assistantResponse}
                """;
        }
    }

    private async Task ReplayRecentContextAsync(ConversationContext context, CancellationToken ct)
    {
        for (var i = 0; i < context.History.Count; i++)
        {
            var turn = context.History[i];
            if (!string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase))
                continue;

            var nextAssistant = context.History
                .Skip(i + 1)
                .FirstOrDefault(t => string.Equals(t.Role, "assistant", StringComparison.OrdinalIgnoreCase));

            await _identityFileUpdateService.UpdateFromTurnAsync(
                turn.Content,
                nextAssistant?.Content ?? string.Empty,
                "engagement-file-maintenance-context",
                ct);
        }
    }

    private EngagementFileVerification VerifyEngagementFiles()
    {
        var agentDir = Path.Combine(_config.Agents.BasePath, "main");
        var agentsPath = Path.Combine(agentDir, "AGENTS.md");
        var selfPath = Path.Combine(agentDir, "SELF.md");
        var userPath = Path.Combine(agentDir, "USER.md");

        return new EngagementFileVerification(
            agentsPath,
            selfPath,
            userPath,
            File.Exists(agentsPath),
            File.Exists(selfPath),
            File.Exists(userPath));
    }

    private static bool IsEngagementFileMaintenanceRequest(string userQuery, string assistantResponse)
    {
        var mentionsEngagementFile = MentionsEngagementFile(userQuery);

        if (!mentionsEngagementFile)
            return false;

        return HasExplicitMaintenanceIntent(userQuery) ||
               HasCorrectiveFileFeedback(userQuery) ||
               AssistantClaimsEngagementFileMutation(assistantResponse);
    }

    private static bool MentionsEngagementFile(string text)
    {
        return Regex.IsMatch(
            text,
            @"\b(?:AGENTS\.md|SELF\.md|USER\.md|engagement files?|identity files?)\b",
            RegexOptions.IgnoreCase);
    }

    private static bool HasExplicitMaintenanceIntent(string userQuery)
    {
        const string verbs = @"(?:create|update|write|initialize|configure|sync|refresh)";
        const string targets = @"(?:AGENTS\.md|SELF\.md|USER\.md|engagement files?|identity files?)";
        return Regex.IsMatch(userQuery, $@"\b{verbs}\b[\s\S]{{0,120}}\b{targets}\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(userQuery, $@"\b{targets}\b[\s\S]{{0,120}}\b{verbs}\b", RegexOptions.IgnoreCase);
    }

    private static bool HasCorrectiveFileFeedback(string userQuery)
    {
        return Regex.IsMatch(
            userQuery,
            @"\b(?:haven't|didn't|did not|failed to|not created|not updated|missing|don't see|do not see|where are)\b",
            RegexOptions.IgnoreCase);
    }

    private static bool AssistantClaimsEngagementFileMutation(string assistantResponse)
    {
        const string claims = @"(?:created|updated|wrote|configured|initialized|synced|refreshed)";
        const string targets = @"(?:AGENTS\.md|SELF\.md|USER\.md|engagement files?|identity files?|files)";
        return Regex.IsMatch(
            assistantResponse,
            $@"\b{claims}\b[\s\S]{{0,120}}\b{targets}\b",
            RegexOptions.IgnoreCase);
    }

    private static string BuildIdentityUpdateMessage(
        string userQuery,
        EngagementIntentClassification classification)
    {
        if (!classification.ShouldUpdate || string.IsNullOrWhiteSpace(classification.NormalizedInsight))
            return userQuery;

        var normalizedInsight = classification.NormalizedInsight.Trim();
        var label = EngagementIntentCategories.ToIdentityUpdateLabel(classification.Category);
        var normalizedLine = $"{label}: {normalizedInsight}";

        return $"{userQuery}\n{normalizedLine}";
    }

    private sealed record EngagementFileVerification(
        string AgentsPath,
        string SelfPath,
        string UserPath,
        bool AgentsPresent,
        bool SelfPresent,
        bool UserPresent)
    {
        public bool AllPresent => AgentsPresent && SelfPresent && UserPresent;

        public string MissingSummary
        {
            get
            {
                var missing = new List<string>();
                if (!AgentsPresent) missing.Add("AGENTS.md");
                if (!SelfPresent) missing.Add("SELF.md");
                if (!UserPresent) missing.Add("USER.md");
                return string.Join(", ", missing);
            }
        }
    }
}
