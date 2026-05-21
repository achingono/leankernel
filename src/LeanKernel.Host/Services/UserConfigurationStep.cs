using Microsoft.Extensions.Logging;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Host.Services;

/// <summary>
/// Handles USER.md configuration step during onboarding.
/// Captures user profile, preferences, and communication patterns.
/// Auto-updates from wiki facts extracted during conversations.
/// </summary>
public sealed class UserConfigurationStep : IUserProfileSynchronizer, IOnboardingStep
{
    private readonly LeanKernelHostPaths _paths;
    private readonly IWikiStore _wikiStore;
    private readonly ILogger<UserConfigurationStep> _logger;

    /// <inheritdoc />
    public string Name => "user";

    /// <summary>
    /// Represents the user configuration step.
    /// </summary>
    public UserConfigurationStep(
        LeanKernelHostPaths paths,
        IWikiStore wikiStore,
        ILogger<UserConfigurationStep> logger)
    {
        _paths = paths;
        _wikiStore = wikiStore;
        _logger = logger;
    }

    /// <summary>
    /// Initialize USER.md if not present, using a template.
    /// </summary>
    public async Task<ConfigurationStepResult> InitializeAsync(CancellationToken ct = default)
    {
        var userPath = GetUserPath();
        var userDir = Path.GetDirectoryName(userPath);

        if (userDir is not null && !Directory.Exists(userDir))
        {
            Directory.CreateDirectory(userDir);
        }

        if (File.Exists(userPath))
        {
            _logger.LogInformation("USER.md already exists at {Path}", userPath);
            return new ConfigurationStepResult
            {
                Success = true,
                Message = "USER.md already configured",
                AlreadyExists = true,
                FilePath = userPath
            };
        }

        // Load template
        var template = await LoadTemplateAsync(ct);

        // Write to file
        await File.WriteAllTextAsync(userPath, template, ct);
        
        _logger.LogInformation("USER.md initialized at {Path}", userPath);

        return new ConfigurationStepResult
        {
            Success = true,
            Message = "USER.md initialized",
            FilePath = userPath
        };
    }

    /// <summary>
    /// Validate USER.md syntax and structure.
    /// </summary>
    public async Task<ConfigurationStepResult> ValidateAsync(CancellationToken ct = default)
    {
        var userPath = GetUserPath();

        if (!File.Exists(userPath))
        {
            return new ConfigurationStepResult
            {
                Success = false,
                Message = "USER.md not found",
                IsValid = false
            };
        }

        try
        {
            var content = await File.ReadAllTextAsync(userPath, ct);
            
            // Basic validation: check for required sections
            var requiredSections = new[]
            {
                "User Profile",
                "Communication Preferences",
                "Knowledge & Expertise"
            };

            var missingSections = requiredSections
                .Where(section => !content.Contains($"## {section}", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (missingSections.Any())
            {
                return new ConfigurationStepResult
                {
                    Success = false,
                    Message = $"USER.md missing sections: {string.Join(", ", missingSections)}",
                    IsValid = false
                };
            }

            return new ConfigurationStepResult
            {
                Success = true,
                Message = "USER.md is valid",
                IsValid = true,
                FilePath = userPath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating USER.md");
            return new ConfigurationStepResult
            {
                Success = false,
                Message = $"Validation error: {ex.Message}",
                IsValid = false
            };
        }
    }

    /// <summary>
    /// Update a specific section of USER.md.
    /// </summary>
    public async Task<ConfigurationStepResult> UpdateSectionAsync(string sectionName, string content, CancellationToken ct = default)
    {
        var userPath = GetUserPath();

        if (!File.Exists(userPath))
        {
            return new ConfigurationStepResult
            {
                Success = false,
                Message = "USER.md not found",
                Errors = ["USER.md has not been initialized"]
            };
        }

        try
        {
            var fileContent = await File.ReadAllTextAsync(userPath, ct);
            var sectionMarker = $"## {sectionName}";

            if (!fileContent.Contains(sectionMarker, StringComparison.OrdinalIgnoreCase))
            {
                return new ConfigurationStepResult
                {
                    Success = false,
                    Message = $"Section '{sectionName}' not found",
                    Errors = [$"Section '{sectionName}' does not exist in USER.md"]
                };
            }

            // Find section bounds
            var startIdx = fileContent.IndexOf(sectionMarker, StringComparison.OrdinalIgnoreCase);
            var endIdx = fileContent.IndexOf("## ", startIdx + 1);
            if (endIdx == -1) endIdx = fileContent.Length;

            // Replace section
            var before = fileContent.Substring(0, startIdx);
            var after = fileContent.Substring(endIdx);
            var updated = $"{before}{sectionMarker}\n\n{content}\n\n{after}";

            await File.WriteAllTextAsync(userPath, updated, ct);
            
            _logger.LogInformation("Updated section '{Section}' in USER.md", sectionName);

            return new ConfigurationStepResult
            {
                Success = true,
                Message = $"Section '{sectionName}' updated successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating USER.md section '{Section}'", sectionName);
            return new ConfigurationStepResult
            {
                Success = false,
                Message = $"Failed to update section: {ex.Message}",
                Errors = [ex.Message]
            };
        }
    }

    /// <summary>
    /// Get the full USER.md content.
    /// </summary>
    public async Task<string> GetUserMdAsync(CancellationToken ct = default)
    {
        var userPath = GetUserPath();

        if (!File.Exists(userPath))
        {
            return "";
        }

        return await File.ReadAllTextAsync(userPath, ct);
    }

    /// <inheritdoc />
    public Task<string> GetMarkdownAsync(CancellationToken ct) => GetUserMdAsync(ct);

    /// <summary>
    /// Update USER.md from extracted wiki facts.
    /// Called by background task to sync user profile from wiki.
    /// </summary>
    public async Task<ConfigurationStepResult> SyncFromWikiAsync(CancellationToken ct = default)
    {
        var userPath = GetUserPath();

        if (!File.Exists(userPath))
        {
            return new ConfigurationStepResult
            {
                Success = false,
                Message = "USER.md not found"
            };
        }

        try
        {
            var currentContent = await File.ReadAllTextAsync(userPath, ct);
            var updatedContent = await ExtractAndMergeWikiFacts(currentContent, ct);

            if (updatedContent != currentContent)
            {
                await File.WriteAllTextAsync(userPath, updatedContent, ct);
                _logger.LogInformation("USER.md synced from wiki facts");
            }

            return new ConfigurationStepResult
            {
                Success = true,
                Message = "USER.md synced from wiki"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing USER.md from wiki");
            return new ConfigurationStepResult
            {
                Success = false,
                Message = $"Sync failed: {ex.Message}",
                Errors = [ex.Message]
            };
        }
    }

    private async Task<string> ExtractAndMergeWikiFacts(string currentContent, CancellationToken ct)
    {
        try
        {
            // Load all user profile and preference facts from wiki
            var query = new Core.Models.WikiQuery
            {
                TextQuery = null,
                MaxResults = 100,
                MinConfidence = 0.0
            };
            
            var userFacts = await _wikiStore.QueryAsync(query, ct);

            // Extract who-user-profile entries
            var profileFacts = userFacts
                .Where(entry => entry.Id == "who-user-profile")
                .SelectMany(entry => entry.Facts.Select(f => f.Claim))
                .Where(IsDurableFact)
                .Distinct()
                .ToList();

            // Extract what-user-preferences entries
            var preferencesFacts = userFacts
                .Where(entry => entry.Id == "what-user-preferences")
                .SelectMany(entry => entry.Facts.Select(f => f.Claim))
                .Where(IsDurableFact)
                .Distinct()
                .ToList();

            // Update the User Profile section if we have new facts
            if (profileFacts.Any())
            {
                var profileSection = string.Join("\n", profileFacts.Select(f => $"- {f}"));
                currentContent = UpdateOrInsertSection(currentContent, "User Profile", profileSection);
            }

            // Update the Preferences section if we have new facts
            if (preferencesFacts.Any())
            {
                var preferencesSection = string.Join("\n", preferencesFacts.Select(f => $"- {f}"));
                currentContent = UpdateOrInsertSection(currentContent, "Preferences & Patterns", preferencesSection);
            }

            return currentContent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract wiki facts for USER.md sync");
            return currentContent;
        }
    }

    private string UpdateOrInsertSection(string content, string sectionName, string newContent)
    {
        var sectionMarker = $"## {sectionName}";

        if (!content.Contains(sectionMarker, StringComparison.OrdinalIgnoreCase))
        {
            // Section doesn't exist, append it
            return $"{content}\n\n{sectionMarker}\n\n{newContent}\n";
        }

        // Find and replace section
        var startIdx = content.IndexOf(sectionMarker, StringComparison.OrdinalIgnoreCase);
        var endIdx = content.IndexOf("## ", startIdx + 1);
        if (endIdx == -1) endIdx = content.Length;

        var before = content.Substring(0, startIdx);
        var after = content.Substring(endIdx);

        return $"{before}{sectionMarker}\n\n{newContent}\n\n{after}";
    }

    private static bool IsDurableFact(string claim)
        => IdentityDurabilityHeuristics.IsDurableFact(claim);

    private string GetUserPath() =>
        Path.Combine(_paths.AgentsDirectory, "main", "USER.md");

    private async Task<string> LoadTemplateAsync(CancellationToken ct)
    {
        var templatePath = Path.Combine(
            AppContext.BaseDirectory, "Templates", "USER.md.template");

        if (File.Exists(templatePath))
        {
            return await File.ReadAllTextAsync(templatePath, ct);
        }

        // Fallback minimal template
        return GenerateFallbackTemplate();
    }

    private string GenerateFallbackTemplate()
    {
        return """
---
version: 1
created: 2024-01-01
updated: 2024-01-01
---

# USER.md - User Profile & Preferences

## Verified Profile

No verified facts yet.

## Verified Preferences and Priorities

No verified facts yet.

## Source-Backed Document Insights

No verified facts yet.

## Tools and Integrations

No verified facts yet.
""";
    }
}
