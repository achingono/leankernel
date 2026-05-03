using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Host.Services;

/// <summary>
/// Loads and parses AGENTS.md from the wiki.
/// </summary>
public sealed class EngagementRulesProvider : IEngagementRulesProvider
{
    private readonly LeanKernelHostPaths _paths;
    private readonly ILogger<EngagementRulesProvider> _logger;
    private EngagementRules? _cached;

    public EngagementRulesProvider(LeanKernelHostPaths paths, ILogger<EngagementRulesProvider> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<EngagementRules> LoadAsync(CancellationToken ct)
    {
        var agentsPath = Path.Combine(_paths.DataDirectory, "wiki", ".LeanKernel", "AGENTS.md");
        
        if (!File.Exists(agentsPath))
        {
            _logger.LogInformation("AGENTS.md not found at {Path}; using defaults", agentsPath);
            _cached = new EngagementRules();
            return _cached;
        }

        try
        {
            var content = await File.ReadAllTextAsync(agentsPath, ct);
            var rules = ParseMarkdown(content);
            
            rules.LastModified = DateTimeOffset.FromFileTime(
                new FileInfo(agentsPath).LastWriteTime.ToFileTime()
            );
            
            _cached = rules;
            _logger.LogInformation("Loaded AGENTS.md from {Path}", agentsPath);
            return rules;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading AGENTS.md; using defaults");
            _cached = new EngagementRules();
            return _cached;
        }
    }

    public EngagementRules GetCurrent()
    {
        return _cached ?? new EngagementRules();
    }

    /// <summary>
    /// Parse AGENTS.md markdown into EngagementRules.
    /// Supports YAML frontmatter and markdown sections.
    /// </summary>
    private static EngagementRules ParseMarkdown(string content)
    {
        var rules = new EngagementRules();
        
        // Extract YAML frontmatter if present
        var frontmatterMatch = Regex.Match(content, @"^---\s*\n(.*?)\n---\s*\n", RegexOptions.Singleline);
        if (frontmatterMatch.Success)
        {
            // Parse YAML for version, etc.
            var yaml = frontmatterMatch.Groups[1].Value;
            if (yaml.Contains("version: 2", StringComparison.OrdinalIgnoreCase))
                rules.Version = "2";
        }

        // Parse sections
        ParsePersonalitySection(content, rules);
        ParseAutonomySection(content, rules);
        ParseTimeBoundariesSection(content, rules);
        ParseChannelRulesSection(content, rules);
        ParseActionFollowUpSection(content, rules);
        ParseMemoryPolicySection(content, rules);
        ParseSafetyBoundariesSection(content, rules);

        return rules;
    }

    private static void ParsePersonalitySection(string content, EngagementRules rules)
    {
        var section = ExtractSection(content, "Agent Personality", "Scope of Autonomy");
        if (string.IsNullOrEmpty(section)) return;

        var toneLine = FindLine(section, "Tone");
        if (toneLine != null && toneLine.Contains("**Tone"))
        {
            rules.Personality.Tone = ExtractValue(toneLine);
        }

        rules.Personality.AllowOpinions = !section.Contains("AllowOpinions.*false", RegexOptions.IgnoreCase);
        rules.Personality.BeResourceful = !section.Contains("BeResourceful.*false", RegexOptions.IgnoreCase);
        rules.Personality.AdmitUncertainty = !section.Contains("AdmitUncertainty.*false", RegexOptions.IgnoreCase);
    }

    private static void ParseAutonomySection(string content, EngagementRules rules)
    {
        var section = ExtractSection(content, "Scope of Autonomy", "Time Boundaries");
        if (string.IsNullOrEmpty(section)) return;

        var canDo = ExtractListItems(section, "CanDoWithoutAsking", "Can Do Without Asking");
        if (canDo.Any())
            rules.Autonomy.CanDoWithoutAsking = canDo.ToArray();

        var mustAsk = ExtractListItems(section, "MustAskBefore", "Must Ask Before");
        if (mustAsk.Any())
            rules.Autonomy.MustAskBefore = mustAsk.ToArray();

        var neverDo = ExtractListItems(section, "NeverDo", "Never Do");
        if (neverDo.Any())
            rules.Autonomy.NeverDo = neverDo.ToArray();
    }

    private static void ParseTimeBoundariesSection(string content, EngagementRules rules)
    {
        var section = ExtractSection(content, "Time Boundaries", "Communication");
        if (string.IsNullOrEmpty(section)) return;

        // Parse timezone
        var tzLine = FindLine(section, "Timezone");
        if (tzLine != null)
            rules.TimeBoundaries.Timezone = ExtractValue(tzLine);

        // Parse active hours
        var activeStart = FindNumberInLine(section, "Active.*Start|Start.*Active");
        if (activeStart.HasValue)
            rules.TimeBoundaries.ActiveHoursStart = activeStart;

        var activeEnd = FindNumberInLine(section, "Active.*End|End.*Active");
        if (activeEnd.HasValue)
            rules.TimeBoundaries.ActiveHoursEnd = activeEnd;

        // Parse Sabbath
        rules.TimeBoundaries.SabbathDay = section.Contains("Saturday", StringComparison.OrdinalIgnoreCase) 
            ? DayOfWeek.Saturday 
            : null;

        rules.TimeBoundaries.AllowSabbathMessages = section.Contains("AllowSabbathMessages.*true", RegexOptions.IgnoreCase);
    }

    private static void ParseChannelRulesSection(string content, EngagementRules rules)
    {
        var section = ExtractSection(content, "Communication", "Action Follow");
        if (string.IsNullOrEmpty(section)) return;

        // Simple parsing: look for channel names (Signal, Discord, Email, etc.)
        foreach (var channel in new[] { "Signal", "Discord", "Email", "DirectChat" })
        {
            var channelLine = FindLine(section, channel);
            if (channelLine != null)
            {
                var ruleSet = new ChannelRuleSet();
                
                if (channelLine.Contains("brief", StringComparison.OrdinalIgnoreCase))
                    ruleSet.Format = "brief";
                else if (channelLine.Contains("verbose", StringComparison.OrdinalIgnoreCase))
                    ruleSet.Format = "verbose";

                rules.ChannelRules.PerChannel[channel] = ruleSet;
            }
        }
    }

    private static void ParseActionFollowUpSection(string content, EngagementRules rules)
    {
        var section = ExtractSection(content, "Action Follow", "Knowledge");
        if (string.IsNullOrEmpty(section)) return;

        rules.ActionFollowUp.AutoTrackFollowUps = !section.Contains("AutoTrack.*false", RegexOptions.IgnoreCase);

        // Extract follow-up days
        var sendMessageMatch = Regex.Match(section, @"SendMessage.*?(\d+)\s*days?", RegexOptions.IgnoreCase);
        if (sendMessageMatch.Success)
            rules.ActionFollowUp.DefaultFollowUpDays["SendMessage"] = int.Parse(sendMessageMatch.Groups[1].Value);
    }

    private static void ParseMemoryPolicySection(string content, EngagementRules rules)
    {
        var section = ExtractSection(content, "Knowledge", "Privacy");
        if (string.IsNullOrEmpty(section)) return;

        var whatToCapture = ExtractListItems(section, "WhatToCapture", "What to Capture");
        if (whatToCapture.Any())
            rules.MemoryPolicy.WhatToCapture = whatToCapture.ToArray();

        rules.MemoryPolicy.UpdateSoulMd = !section.Contains("UpdateSoulMd.*false", RegexOptions.IgnoreCase);
        rules.MemoryPolicy.UpdateUserMd = !section.Contains("UpdateUserMd.*false", RegexOptions.IgnoreCase);
    }

    private static void ParseSafetyBoundariesSection(string content, EngagementRules rules)
    {
        var section = ExtractSection(content, "Privacy", "---");
        if (string.IsNullOrEmpty(section)) return;

        rules.SafetyBoundaries.AllowExternalDataExport = section.Contains("AllowExternalDataExport.*true", RegexOptions.IgnoreCase);
        rules.SafetyBoundaries.RequireEmailDraft = !section.Contains("RequireEmailDraft.*false", RegexOptions.IgnoreCase);
        rules.SafetyBoundaries.RequireCodeReview = !section.Contains("RequireCodeReview.*false", RegexOptions.IgnoreCase);
    }

    private static string ExtractSection(string content, string sectionStart, string sectionEnd)
    {
        var startIdx = content.IndexOf($"## {sectionStart}", StringComparison.OrdinalIgnoreCase);
        if (startIdx == -1) return "";

        var endIdx = content.IndexOf($"## {sectionEnd}", startIdx, StringComparison.OrdinalIgnoreCase);
        if (endIdx == -1) endIdx = content.Length;

        return content.Substring(startIdx, endIdx - startIdx);
    }

    private static string? FindLine(string content, string search)
    {
        var lines = content.Split('\n');
        return lines.FirstOrDefault(l => l.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private static int? FindNumberInLine(string content, string pattern)
    {
        var match = Regex.Match(content, pattern + @".*?(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var num))
            return num;
        return null;
    }

    private static string ExtractValue(string line)
    {
        // Extract value after colon or equals
        var colonIdx = line.IndexOf(':');
        if (colonIdx != -1)
            return line.Substring(colonIdx + 1).Trim().Trim('`', '"', '*');

        var eqIdx = line.IndexOf('=');
        if (eqIdx != -1)
            return line.Substring(eqIdx + 1).Trim().Trim('`', '"', '*');

        return "";
    }

    private static List<string> ExtractListItems(string content, string listName, string sectionTitle)
    {
        var items = new List<string>();
        var lines = content.Split('\n');

        var inList = false;
        foreach (var line in lines)
        {
            if (line.Contains(listName, StringComparison.OrdinalIgnoreCase) ||
                line.Contains(sectionTitle, StringComparison.OrdinalIgnoreCase))
            {
                inList = true;
                continue;
            }

            if (inList)
            {
                if (line.StartsWith("##")) break;
                if (line.StartsWith("- "))
                {
                    var item = line.Substring(2).Trim().Trim('`', '*', '"');
                    if (!string.IsNullOrWhiteSpace(item))
                        items.Add(item);
                }
            }
        }

        return items;
    }
}
