using Microsoft.Extensions.Logging;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Services;

/// <summary>
/// Handles AGENTS.md configuration step during onboarding.
/// </summary>
public sealed class AgentsConfigurationStep
{
    private readonly LeanKernelHostPaths _paths;
    private readonly IEngagementRulesProvider _rulesProvider;
    private readonly ILogger<AgentsConfigurationStep> _logger;

    public AgentsConfigurationStep(
        LeanKernelHostPaths paths,
        IEngagementRulesProvider rulesProvider,
        ILogger<AgentsConfigurationStep> logger)
    {
        _paths = paths;
        _rulesProvider = rulesProvider;
        _logger = logger;
    }

    /// <summary>
    /// Initialize AGENTS.md if not present, using a template.
    /// </summary>
    public async Task<AgentsStepResult> InitializeAsync(string? preset = "basic", CancellationToken ct = default)
    {
        var agentsPath = Path.Combine(_paths.DataDirectory, "wiki", ".LeanKernel", "AGENTS.md");
        var agentsDir = Path.GetDirectoryName(agentsPath);

        if (!Directory.Exists(agentsDir))
        {
            Directory.CreateDirectory(agentsDir);
        }

        if (File.Exists(agentsPath))
        {
            _logger.LogInformation("AGENTS.md already exists at {Path}", agentsPath);
            return new AgentsStepResult
            {
                Success = true,
                Message = "AGENTS.md already configured",
                AlreadyExists = true
            };
        }

        // Load template and customize based on preset
        var template = await LoadTemplateAsync(ct);
        var customized = CustomizeForPreset(template, preset ?? "basic");

        // Write to file
        await File.WriteAllTextAsync(agentsPath, customized, ct);
        
        _logger.LogInformation("AGENTS.md initialized at {Path} with preset '{Preset}'", agentsPath, preset);

        // Load the rules so they're available immediately
        await _rulesProvider.LoadAsync(ct);

        return new AgentsStepResult
        {
            Success = true,
            Message = $"AGENTS.md initialized with '{preset}' preset",
            AgentsPath = agentsPath,
            PresetUsed = preset
        };
    }

    /// <summary>
    /// Validate AGENTS.md syntax and structure.
    /// </summary>
    public async Task<AgentsStepResult> ValidateAsync(CancellationToken ct = default)
    {
        var agentsPath = Path.Combine(_paths.DataDirectory, "wiki", ".LeanKernel", "AGENTS.md");

        if (!File.Exists(agentsPath))
        {
            return new AgentsStepResult
            {
                Success = false,
                Message = "AGENTS.md not found",
                IsValid = false
            };
        }

        try
        {
            var content = await File.ReadAllTextAsync(agentsPath, ct);
            
            // Basic validation: check for required sections
            var requiredSections = new[]
            {
                "Agent Personality",
                "Scope of Autonomy",
                "Time Boundaries"
            };

            var missingSections = requiredSections
                .Where(section => !content.Contains($"## {section}", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (missingSections.Any())
            {
                return new AgentsStepResult
                {
                    Success = false,
                    Message = $"AGENTS.md missing sections: {string.Join(", ", missingSections)}",
                    IsValid = false
                };
            }

            // Try to load and parse rules
            var rules = await _rulesProvider.LoadAsync(ct);
            if (rules == null)
            {
                return new AgentsStepResult
                {
                    Success = false,
                    Message = "Failed to parse AGENTS.md",
                    IsValid = false
                };
            }

            return new AgentsStepResult
            {
                Success = true,
                Message = "AGENTS.md is valid",
                IsValid = true,
                AgentsPath = agentsPath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating AGENTS.md");
            return new AgentsStepResult
            {
                Success = false,
                Message = $"Validation error: {ex.Message}",
                IsValid = false
            };
        }
    }

    /// <summary>
    /// Get available onboarding presets.
    /// </summary>
    public IReadOnlyList<AgentsPreset> GetAvailablePresets()
    {
        return new[]
        {
            new AgentsPreset
            {
                Name = "basic",
                DisplayName = "Basic (Recommended)",
                Description = "Conservative defaults: cautious autonomy, limited channels, strict safety",
                CanDoCount = 7,
                MustAskCount = 6
            },
            new AgentsPreset
            {
                Name = "autonomous",
                DisplayName = "Autonomous",
                Description = "Trust-based: broad autonomy, many auto-approved actions, active hours only",
                CanDoCount = 15,
                MustAskCount = 5
            },
            new AgentsPreset
            {
                Name = "cautious",
                DisplayName = "Cautious",
                Description = "Paranoid-safe: minimal autonomy, requires approval for most actions",
                CanDoCount = 3,
                MustAskCount = 12
            }
        };
    }

    private async Task<string> LoadTemplateAsync(CancellationToken ct)
    {
        var templatePath = Path.Combine(
            AppContext.BaseDirectory, "Templates", "AGENTS.md.template");

        if (File.Exists(templatePath))
        {
            return await File.ReadAllTextAsync(templatePath, ct);
        }

        // Fallback minimal template
        return GenerateFallbackTemplate();
    }

    private string CustomizeForPreset(string template, string preset)
    {
        return preset.ToLowerInvariant() switch
        {
            "autonomous" => ReplaceSection(template,
                "CanDoWithoutAsking",
                new[] { "ViewRepositoryStructure", "ReadPublicDocumentation", "SearchCodebase", "ViewConfiguration", "ViewErrorLogs", "CreateBranch", "CreateTestCommits", "ViewDeploymentStatus", "RunTests", "ViewBuildLogs", "ReadWiki", "ViewAnalytics", "CheckSystemHealth", "UpdateDocumentation", "CommentOnPRs" }),
            "cautious" => ReplaceSection(template,
                "CanDoWithoutAsking",
                new[] { "ViewRepositoryStructure", "SearchCodebase" }),
            _ => template // basic preset (default)
        };
    }

    private string ReplaceSection(string template, string sectionName, string[] items)
    {
        // Simple replacement: find section and replace list items
        var sectionMarker = $"### {sectionName}";
        if (!template.Contains(sectionMarker))
        {
            return template;
        }

        var itemsList = string.Join("\n", items.Select(a => $"- {a}"));
        var startIdx = template.IndexOf(sectionMarker, StringComparison.OrdinalIgnoreCase);
        if (startIdx == -1) return template;

        var endIdx = template.IndexOf("###", startIdx + 1);
        if (endIdx == -1) endIdx = template.Length;

        var before = template.Substring(0, startIdx);
        var after = template.Substring(endIdx);

        return $"{before}{sectionMarker}\n\n{itemsList}\n\n{after}";
    }

    private string GenerateFallbackTemplate()
    {
        return """
---
version: 2
created: 2024-01-01
---

# AGENTS.md - Rules of Engagement

## Agent Personality

**Tone:** Professional and helpful

## Scope of Autonomy

### Can Do Without Asking

- ViewRepositoryStructure
- SearchCodebase

### Must Ask Before

- PushToMainBranch
- DeleteBranch

### Never Do

- CommitSecrets
- DeleteProductionData

## Time Boundaries

**Timezone:** UTC
**Active Hours:** 8:00 AM - 10:00 PM

## Communication Rules

Communication preferences for different channels.

## Action Follow-Up

Tracking and follow-up expectations.

## Knowledge Management

What the agent should remember about you.

## Privacy & Safety

Data handling and safety constraints.
""";
    }
}

/// <summary>
/// Result of AGENTS.md configuration step.
/// </summary>
public sealed class AgentsStepResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public bool AlreadyExists { get; init; }
    public bool? IsValid { get; init; }
    public string? AgentsPath { get; init; }
    public string? PresetUsed { get; init; }
}

/// <summary>
/// Available preset for AGENTS.md onboarding.
/// </summary>
public sealed class AgentsPreset
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public int CanDoCount { get; init; }
    public int MustAskCount { get; init; }
}
