using LeanKernel.Core.Configuration;
using LeanKernel.Core.Models;

namespace LeanKernel.Host.Services;

/// <summary>
/// Represents the lean kernel host paths.
/// </summary>
public sealed class LeanKernelHostPaths
{
    /// <summary>
    /// Gets or sets the data directory.
    /// </summary>
    public required string DataDirectory { get; init; }
    /// <summary>
    /// Gets or sets the agents directory.
    /// </summary>
    public required string AgentsDirectory { get; init; }
    /// <summary>
    /// Gets or sets the runtime config path.
    /// </summary>
    public required string RuntimeConfigPath { get; init; }
    /// <summary>
    /// Gets or sets the onboarding state path.
    /// </summary>
    public required string OnboardingStatePath { get; init; }
    /// <summary>
    /// Gets or sets the lite llm config path.
    /// </summary>
    public string LiteLlmConfigPath { get; init; } = "";
}

/// <summary>
/// Represents the agents config preset.
/// </summary>
public sealed class AgentsConfigPreset
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public required string DisplayName { get; init; }
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public required string Description { get; init; }
}

/// <summary>
/// Represents the agents initialize request.
/// </summary>
public sealed class AgentsInitializeRequest
{
    /// <summary>
    /// Gets or sets the preset name.
    /// </summary>
    public required string PresetName { get; init; }
}

/// <summary>
/// Represents the agents initialize response.
/// </summary>
public sealed class AgentsInitializeResponse
{
    /// <summary>
    /// Gets or sets the success.
    /// </summary>
    public bool Success { get; init; }
    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    public string Message { get; init; } = "";
    /// <summary>
    /// Gets or sets the rules.
    /// </summary>
    public EngagementRules? Rules { get; init; }
}

/// <summary>
/// Represents the agents validate response.
/// </summary>
public sealed class AgentsValidateResponse
{
    /// <summary>
    /// Gets or sets the success.
    /// </summary>
    public bool Success { get; init; }
    /// <summary>
    /// Gets or sets the is valid.
    /// </summary>
    public bool IsValid { get; init; }
    /// <summary>
    /// Gets or sets the errors.
    /// </summary>
    public List<string> Errors { get; init; } = [];
    /// <summary>
    /// Gets or sets the warnings.
    /// </summary>
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Represents the agents section update request.
/// </summary>
public sealed class AgentsSectionUpdateRequest
{
    /// <summary>
    /// Gets or sets the section name.
    /// </summary>
    public required string SectionName { get; init; }
    /// <summary>
    /// Gets or sets the content.
    /// </summary>
    public required string Content { get; init; }
}
