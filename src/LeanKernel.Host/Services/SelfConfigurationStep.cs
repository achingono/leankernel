using Microsoft.Extensions.Logging;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Host.Services;

/// <summary>
/// Handles SELF.md configuration step during onboarding.
/// Captures the agent's self-definition: personality, capabilities, and preferences.
/// </summary>
public sealed class SelfConfigurationStep : IAgentSelfProfileInitializer
{
    private readonly LeanKernelHostPaths _paths;
    private readonly ILogger<SelfConfigurationStep> _logger;

    public SelfConfigurationStep(
        LeanKernelHostPaths paths,
        ILogger<SelfConfigurationStep> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Initialize SELF.md if not present, using a template.
    /// </summary>
    public async Task<ConfigurationStepResult> InitializeAsync(CancellationToken ct = default)
    {
        var selfPath = GetSelfPath();
        var selfDir = Path.GetDirectoryName(selfPath);

        if (selfDir is not null && !Directory.Exists(selfDir))
        {
            Directory.CreateDirectory(selfDir);
        }

        if (File.Exists(selfPath))
        {
            _logger.LogInformation("SELF.md already exists at {Path}", selfPath);
            return new ConfigurationStepResult
            {
                Success = true,
                Message = "SELF.md already configured",
                AlreadyExists = true,
                FilePath = selfPath
            };
        }

        // Load template
        var template = await LoadTemplateAsync(ct);

        // Write to file
        await File.WriteAllTextAsync(selfPath, template, ct);
        
        _logger.LogInformation("SELF.md initialized at {Path}", selfPath);

        return new ConfigurationStepResult
        {
            Success = true,
            Message = "SELF.md initialized",
            FilePath = selfPath
        };
    }

    /// <summary>
    /// Validate SELF.md syntax and structure.
    /// </summary>
    public async Task<ConfigurationStepResult> ValidateAsync(CancellationToken ct = default)
    {
        var selfPath = GetSelfPath();

        if (!File.Exists(selfPath))
        {
            return new ConfigurationStepResult
            {
                Success = false,
                Message = "SELF.md not found",
                IsValid = false
            };
        }

        try
        {
            var content = await File.ReadAllTextAsync(selfPath, ct);
            
            // Basic validation: check for required sections
            var requiredSections = new[]
            {
                "Agent Identity",
                "Core Capabilities",
                "Knowledge Domains"
            };

            var missingSections = requiredSections
                .Where(section => !content.Contains($"## {section}", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (missingSections.Any())
            {
                return new ConfigurationStepResult
                {
                    Success = false,
                    Message = $"SELF.md missing sections: {string.Join(", ", missingSections)}",
                    IsValid = false
                };
            }

            return new ConfigurationStepResult
            {
                Success = true,
                Message = "SELF.md is valid",
                IsValid = true,
                FilePath = selfPath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating SELF.md");
            return new ConfigurationStepResult
            {
                Success = false,
                Message = $"Validation error: {ex.Message}",
                IsValid = false
            };
        }
    }

    /// <summary>
    /// Update a specific section of SELF.md.
    /// </summary>
    public async Task<ConfigurationStepResult> UpdateSectionAsync(string sectionName, string content, CancellationToken ct = default)
    {
        var selfPath = GetSelfPath();

        if (!File.Exists(selfPath))
        {
            return new ConfigurationStepResult
            {
                Success = false,
                Message = "SELF.md not found",
                Errors = ["SELF.md has not been initialized"]
            };
        }

        try
        {
            var fileContent = await File.ReadAllTextAsync(selfPath, ct);
            var sectionMarker = $"## {sectionName}";

            if (!fileContent.Contains(sectionMarker, StringComparison.OrdinalIgnoreCase))
            {
                return new ConfigurationStepResult
                {
                    Success = false,
                    Message = $"Section '{sectionName}' not found",
                    Errors = [$"Section '{sectionName}' does not exist in SELF.md"]
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

            await File.WriteAllTextAsync(selfPath, updated, ct);
            
            _logger.LogInformation("Updated section '{Section}' in SELF.md", sectionName);

            return new ConfigurationStepResult
            {
                Success = true,
                Message = $"Section '{sectionName}' updated successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating SELF.md section '{Section}'", sectionName);
            return new ConfigurationStepResult
            {
                Success = false,
                Message = $"Failed to update section: {ex.Message}",
                Errors = [ex.Message]
            };
        }
    }

    /// <summary>
    /// Get the full SELF.md content.
    /// </summary>
    public async Task<string> GetSelfMdAsync(CancellationToken ct = default)
    {
        var selfPath = GetSelfPath();

        if (!File.Exists(selfPath))
        {
            return "";
        }

        return await File.ReadAllTextAsync(selfPath, ct);
    }

    private string GetSelfPath() =>
        Path.Combine(_paths.AgentsDirectory, "main", "SELF.md");

    private async Task<string> LoadTemplateAsync(CancellationToken ct)
    {
        var templatePath = Path.Combine(
            AppContext.BaseDirectory, "Templates", "SELF.md.template");

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

# SELF.md - Agent Self-Definition

## Agent Identity

**Name:** LeanKernel Agent
**Purpose:** Autonomous software engineering assistant
**Founding Philosophy:** Precision, transparency, continuous improvement

## Core Capabilities

- Code analysis and refactoring
- Architecture planning
- Documentation generation
- Test suite design
- Performance optimization

## Knowledge Domains

- Software architecture patterns
- Full-stack development
- DevOps and deployment
- Testing strategies
- Technical writing

## Learning Preferences

- From code examples and patterns
- Through structured feedback loops
- Via user preferences (captured in USER.md)
- From contextual cues in chat

## Communication Style

- Clear and direct
- Technical but accessible
- Action-oriented
- Collaborative

## Ethical Boundaries

- Privacy-first data handling
- Transparency about limitations
- Respect for user intent
- No secret operations
""";
    }
}
