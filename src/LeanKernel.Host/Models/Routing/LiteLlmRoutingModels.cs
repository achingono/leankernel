namespace LeanKernel.Host.Models.Routing;

// ── Deserialized view of config/litellm/config.yaml ───────────────────────────

/// <summary>
/// Represents the lite llm routing config.
/// </summary>
public sealed class LiteLlmRoutingConfig
{
    /// <summary>
    /// Gets or sets the providers.
    /// </summary>
    public Dictionary<string, ProviderSpec> Providers { get; set; } = [];
    /// <summary>
    /// Gets or sets the routes.
    /// </summary>
    public Dictionary<string, List<RouteEntry>> Routes { get; set; } = [];
    /// <summary>
    /// Gets or sets the aliases.
    /// </summary>
    public Dictionary<string, string> Aliases { get; set; } = [];
    /// <summary>
    /// Gets or sets the router.
    /// </summary>
    public RouterPolicy Router { get; set; } = new();
}

/// <summary>
/// Represents the provider spec.
/// </summary>
public sealed class ProviderSpec
{
    /// <summary>
    /// Gets or sets the litellm provider.
    /// </summary>
    public string? LitellmProvider { get; set; }
    /// <summary>
    /// Gets or sets the keys.
    /// </summary>
    public List<KeySlot> Keys { get; set; } = [];
    /// <summary>
    /// Gets or sets the base url.
    /// </summary>
    public List<KeySlot>? BaseUrl { get; set; }
    /// <summary>
    /// Gets or sets the models.
    /// </summary>
    public List<ModelSpec> Models { get; set; } = [];
}

/// <summary>
/// Represents the key slot.
/// </summary>
public sealed class KeySlot
{
    /// <summary>
    /// Gets or sets the source.
    /// </summary>
    public string Source { get; set; } = "env";
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string? Name { get; set; }
    /// <summary>
    /// Gets or sets the api base env.
    /// </summary>
    public string? ApiBaseEnv { get; set; }
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Represents the model spec.
/// </summary>
public sealed class ModelSpec
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public string Id { get; set; } = "";
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// Gets or sets the max tokens.
    /// </summary>
    public int MaxTokens { get; set; }
    /// <summary>
    /// Gets or sets the mode.
    /// </summary>
    public string? Mode { get; set; }
    /// <summary>
    /// Gets or sets the dimensions.
    /// </summary>
    public int? Dimensions { get; set; }
    /// <summary>
    /// Gets or sets the use responses api.
    /// </summary>
    public bool? UseResponsesApi { get; set; }
}

/// <summary>
/// Represents the route entry.
/// </summary>
public sealed class RouteEntry
{
    /// <summary>
    /// Gets or sets the provider.
    /// </summary>
    public string Provider { get; set; } = "";
    /// <summary>
    /// Gets or sets the model.
    /// </summary>
    public string Model { get; set; } = "";
    /// <summary>
    /// Gets or sets the keys.
    /// </summary>
    public List<string> Keys { get; set; } = [];
    /// <summary>
    /// Gets or sets the order.
    /// </summary>
    public int Order { get; set; }
}

/// <summary>
/// Represents the router policy.
/// </summary>
public sealed class RouterPolicy
{
    /// <summary>
    /// Gets or sets the routing strategy.
    /// </summary>
    public string RoutingStrategy { get; set; } = "least-busy";
    /// <summary>
    /// Gets or sets the enable pre call checks.
    /// </summary>
    public bool EnablePreCallChecks { get; set; } = true;
    /// <summary>
    /// Gets or sets the num retries.
    /// </summary>
    public int NumRetries { get; set; } = 7;
    /// <summary>
    /// Gets or sets the retry after.
    /// </summary>
    public int RetryAfter { get; set; } = 5;
    /// <summary>
    /// Gets or sets the timeout.
    /// </summary>
    public int Timeout { get; set; } = 120;
}

// ── API response / request models ─────────────────────────────────────────────

/// <summary>
/// Represents the routing config response.
/// </summary>
public sealed class RoutingConfigResponse
{
    /// <summary>
    /// Gets or sets the config.
    /// </summary>
    public LiteLlmRoutingConfig Config { get; init; } = new();
    /// <summary>
    /// Gets or sets the validation errors.
    /// </summary>
    public List<RoutingValidationError> ValidationErrors { get; init; } = [];
    /// <summary>
    /// Gets or sets the key statuses.
    /// </summary>
    public List<ProviderKeyStatus> KeyStatuses { get; init; } = [];
}

/// <summary>
/// Represents the provider key status.
/// </summary>
public sealed class ProviderKeyStatus
{
    /// <summary>
    /// Gets or sets the provider.
    /// </summary>
    public string Provider { get; init; } = "";
    /// <summary>
    /// Gets or sets the slot name.
    /// </summary>
    public string SlotName { get; init; } = "";
    /// <summary>
    /// Gets or sets the env var.
    /// </summary>
    public string EnvVar { get; init; } = "";
    /// <summary>
    /// Gets or sets the configured.
    /// </summary>
    public bool Configured { get; init; }
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Represents the routing validation error.
/// </summary>
public sealed class RoutingValidationError
{
    /// <summary>
    /// Gets or sets the code.
    /// </summary>
    public string Code { get; init; } = "";
    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    public string Message { get; init; } = "";
    /// <summary>
    /// Gets or sets the severity.
    /// </summary>
    public string? Severity { get; init; } = "error"; // "error" | "warning"
}

/// <summary>
/// Represents the routing config save request.
/// </summary>
public sealed class RoutingConfigSaveRequest
{
    /// <summary>
    /// Gets or sets the config.
    /// </summary>
    public LiteLlmRoutingConfig Config { get; init; } = new();
    /// <summary>
    /// Gets or sets the dry run.
    /// </summary>
    public bool DryRun { get; init; }
}

/// <summary>
/// Represents the routing config save response.
/// </summary>
public sealed class RoutingConfigSaveResponse
{
    /// <summary>
    /// Gets or sets the saved.
    /// </summary>
    public bool Saved { get; init; }
    /// <summary>
    /// Gets or sets the yaml diff.
    /// </summary>
    public string YamlDiff { get; init; } = "";
    /// <summary>
    /// Gets or sets the validation errors.
    /// </summary>
    public List<RoutingValidationError> ValidationErrors { get; init; } = [];
}

/// <summary>
/// Represents the raw yaml save request.
/// </summary>
public sealed class RawYamlSaveRequest
{
    /// <summary>
    /// Gets or sets the yaml.
    /// </summary>
    public string Yaml { get; init; } = "";
}
