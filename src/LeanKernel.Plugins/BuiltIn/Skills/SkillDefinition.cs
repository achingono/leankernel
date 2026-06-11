namespace LeanKernel.Plugins.BuiltIn.Skills;

public sealed record SkillDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public Dictionary<string, object?> Metadata { get; init; } = new();
    public required SkillRuntimeConfig Runtime { get; init; }
    public required IReadOnlyList<SkillOperation> Operations { get; init; }
    public string? SourcePath { get; init; }
}

public sealed record SkillRuntimeConfig
{
    public string Type { get; init; } = "cli";
    public string? Command { get; init; }
    public string? BaseUrl { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public SkillAuthConfig Auth { get; init; } = new();
    public SkillRequiresConfig Requires { get; init; } = new();
    public SkillEgressConfig Egress { get; init; } = new();
}

public sealed record SkillAuthConfig
{
    public string Type { get; init; } = "none";
    public string? SecretRef { get; init; }
}

public sealed record SkillRequiresConfig
{
    public IReadOnlyList<SkillBinConfig> Bins { get; init; } = Array.Empty<SkillBinConfig>();
}

public sealed record SkillBinConfig
{
    public required string Name { get; init; }
    public string? MinVersion { get; init; }
    public string? ChecksumSha256 { get; init; }
}

public sealed record SkillEgressConfig
{
    public IReadOnlyList<string> AllowHosts { get; init; } = Array.Empty<string>();
}

public sealed record SkillOperation
{
    public required string Id { get; init; }
    public required string Summary { get; init; }
    public SkillInvokeConfig Invoke { get; init; } = new();
    public Dictionary<string, object?>? ParametersRaw { get; init; }
}

public sealed record SkillInvokeConfig
{
    public IReadOnlyList<string> Argv { get; init; } = Array.Empty<string>();
    public Dictionary<string, string> Flags { get; init; } = new();
    public string? HttpMethod { get; init; }
    public string? HttpPath { get; init; }
}
