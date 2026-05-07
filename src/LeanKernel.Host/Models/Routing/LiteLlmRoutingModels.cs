namespace LeanKernel.Host.Models.Routing;

// ── Deserialized view of config/litellm/config.yaml ───────────────────────────

public sealed class LiteLlmRoutingConfig
{
    public Dictionary<string, ProviderSpec> Providers { get; set; } = [];
    public Dictionary<string, List<RouteEntry>> Routes { get; set; } = [];
    public Dictionary<string, string> Aliases { get; set; } = [];
    public RouterPolicy Router { get; set; } = new();
}

public sealed class ProviderSpec
{
    public string? LitellmProvider { get; set; }
    public List<KeySlot> Keys { get; set; } = [];
    public List<KeySlot>? BaseUrl { get; set; }
    public List<ModelSpec> Models { get; set; } = [];
}

public sealed class KeySlot
{
    public string Source { get; set; } = "env";
    public string? Name { get; set; }
    public string? ApiBaseEnv { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class ModelSpec
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int MaxTokens { get; set; }
    public string? Mode { get; set; }
    public int? Dimensions { get; set; }
    public bool? UseResponsesApi { get; set; }
}

public sealed class RouteEntry
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public List<string> Keys { get; set; } = [];
    public int Order { get; set; }
}

public sealed class RouterPolicy
{
    public string RoutingStrategy { get; set; } = "least-busy";
    public bool EnablePreCallChecks { get; set; } = true;
    public int NumRetries { get; set; } = 7;
    public int RetryAfter { get; set; } = 5;
    public int Timeout { get; set; } = 120;
}

// ── API response / request models ─────────────────────────────────────────────

public sealed class RoutingConfigResponse
{
    public LiteLlmRoutingConfig Config { get; init; } = new();
    public List<RoutingValidationError> ValidationErrors { get; init; } = [];
    public List<ProviderKeyStatus> KeyStatuses { get; init; } = [];
}

public sealed class ProviderKeyStatus
{
    public string Provider { get; init; } = "";
    public string SlotName { get; init; } = "";
    public string EnvVar { get; init; } = "";
    public bool Configured { get; init; }
    public bool Enabled { get; init; } = true;
}

public sealed class RoutingValidationError
{
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public string? Severity { get; init; } = "error"; // "error" | "warning"
}

public sealed class RoutingConfigSaveRequest
{
    public LiteLlmRoutingConfig Config { get; init; } = new();
    public bool DryRun { get; init; }
}

public sealed class RoutingConfigSaveResponse
{
    public bool Saved { get; init; }
    public string YamlDiff { get; init; } = "";
    public List<RoutingValidationError> ValidationErrors { get; init; } = [];
}
