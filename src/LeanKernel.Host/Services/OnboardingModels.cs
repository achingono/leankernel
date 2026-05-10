using LeanKernel.Core.Configuration;
using LeanKernel.Core.Models;

namespace LeanKernel.Host.Services;

public sealed class LeanKernelHostPaths
{
    public required string DataDirectory { get; init; }
    public required string AgentsDirectory { get; init; }
    public required string RuntimeConfigPath { get; init; }
    public required string OnboardingStatePath { get; init; }
    public string LiteLlmConfigPath { get; init; } = "";
}

public sealed class AgentsConfigPreset
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
}

public sealed class AgentsInitializeRequest
{
    public required string PresetName { get; init; }
}

public sealed class AgentsInitializeResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public EngagementRules? Rules { get; init; }
}

public sealed class AgentsValidateResponse
{
    public bool Success { get; init; }
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

public sealed class AgentsSectionUpdateRequest
{
    public required string SectionName { get; init; }
    public required string Content { get; init; }
}
