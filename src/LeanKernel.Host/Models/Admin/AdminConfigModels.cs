using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Models.Admin;

/// <summary>
/// Top-level response for the admin config API.
/// Covers every supported <see cref="LeanKernelConfig"/> section with provenance metadata.
/// </summary>
public sealed record AdminConfigResponse
{
    public LiteLlmConfigSection LiteLlm { get; init; } = new();
    public QdrantConfigSection Qdrant { get; init; } = new();
    public SignalConfigSection Signal { get; init; } = new();
    public UnstructuredConfigSection Unstructured { get; init; } = new();
    public WikiConfigSection Wiki { get; init; } = new();
    public AgentsConfigSection Agents { get; init; } = new();
    public KnowledgeConfigSection Knowledge { get; init; } = new();
    public ContextConfigSection Context { get; init; } = new();
    public SchedulerConfigSection Scheduler { get; init; } = new();
    public AuthConfigSection Auth { get; init; } = new();
    public RoutingConfigSection Routing { get; init; } = new();
    public EngagementConfigSection Engagement { get; init; } = new();
    public ChannelsConfigSection Channels { get; init; } = new();
}

/// <summary>
/// Annotated field value: carries the display-safe value plus provenance metadata.
/// </summary>
public sealed record ConfigField
{
    /// <summary>Display-safe value (secrets are masked or shown as reference only).</summary>
    public object? Value { get; init; }

    /// <summary>True when the value is a secret and has been masked.</summary>
    public bool Masked { get; init; }

    /// <summary>True when the value is expected to come from an environment variable.</summary>
    public bool EnvBacked { get; init; }

    /// <summary>True when changing this field requires a service restart.</summary>
    public bool RestartRequired { get; init; }

    /// <summary>True when this field can be changed via the admin UI at runtime.</summary>
    public bool Mutable { get; init; } = true;

    /// <summary>Short description explaining the field's purpose.</summary>
    public string? Description { get; init; }
}

// ── Section records ──────────────────────────────────────────────────────────

public sealed record LiteLlmConfigSection
{
    public string SectionLabel { get; } = "LiteLLM Connectivity";
    public bool RestartRequired { get; } = false;

    public ConfigField BaseUrl { get; init; } = new();
    public ConfigField ApiKey { get; init; } = new();
    public ConfigField DefaultModel { get; init; } = new();
    public ConfigField EmbeddingModel { get; init; } = new();
    public ConfigField ContextWindowTokens { get; init; } = new();
}

public sealed record QdrantConfigSection
{
    public string SectionLabel { get; } = "Qdrant Vector Store";
    public bool RestartRequired { get; } = true;

    public ConfigField Host { get; init; } = new();
    public ConfigField Port { get; init; } = new();
    public ConfigField CollectionName { get; init; } = new();
    public ConfigField EmbeddingDimension { get; init; } = new();
}

public sealed record SignalConfigSection
{
    public string SectionLabel { get; } = "Signal Integration";
    public bool RestartRequired { get; } = false;

    public ConfigField CliPath { get; init; } = new();
    public ConfigField Account { get; init; } = new();
    public ConfigField Enabled { get; init; } = new();
    public ConfigField AllowedSenders { get; init; } = new();
    public ConfigField DaemonBaseUrl { get; init; } = new();
}

public sealed record UnstructuredConfigSection
{
    public string SectionLabel { get; } = "Unstructured Document Processing";
    public bool RestartRequired { get; } = false;

    public ConfigField Enabled { get; init; } = new();
    public ConfigField BaseUrl { get; init; } = new();
    public ConfigField TimeoutSeconds { get; init; } = new();
    public ConfigField SupportedMimeTypes { get; init; } = new();
    public ConfigField SupportedExtensions { get; init; } = new();
}

public sealed record WikiConfigSection
{
    public string SectionLabel { get; } = "Wiki Storage";
    public bool RestartRequired { get; } = false;

    public ConfigField BasePath { get; init; } = new();
    public ConfigField MaxFactsPerEntry { get; init; } = new();
    public ConfigField StaleFactDays { get; init; } = new();
    public ConfigField MinConfidenceThreshold { get; init; } = new();
}

public sealed record AgentsConfigSection
{
    public string SectionLabel { get; } = "Agent Behavior";
    public bool RestartRequired { get; } = false;

    public ConfigField BasePath { get; init; } = new();
}

public sealed record KnowledgeConfigSection
{
    public string SectionLabel { get; } = "Knowledge Retrieval";
    public bool RestartRequired { get; } = false;

    public ConfigField Enabled { get; init; } = new();
    public ConfigField CollectionName { get; init; } = new();
    public ConfigField EmbeddingDimension { get; init; } = new();
    public ConfigField DocumentsPath { get; init; } = new();
    public ConfigField DefaultDocumentTags { get; init; } = new();
}

public sealed record ContextConfigSection
{
    public string SectionLabel { get; } = "Context Retrieval Weights";
    public bool RestartRequired { get; } = false;

    public ConfigField SemanticSimilarityWeight { get; init; } = new();
    public ConfigField RecencyDecayWeight { get; init; } = new();
    public ConfigField DimensionMatchWeight { get; init; } = new();
    public ConfigField InteractionFrequencyWeight { get; init; } = new();
    public ConfigField MinRelevanceThreshold { get; init; } = new();
    public ConfigField MaxConversationTurns { get; init; } = new();
}

public sealed record SchedulerConfigSection
{
    public string SectionLabel { get; } = "Scheduler";
    public bool RestartRequired { get; } = false;

    public ConfigField Enabled { get; init; } = new();
    public ConfigField WikiMaintenanceCron { get; init; } = new();
    public ConfigField ChatFactScrubCron { get; init; } = new();
}

public sealed record AuthConfigSection
{
    public string SectionLabel { get; } = "Authentication";
    public bool RestartRequired { get; } = true;

    public ConfigField Mode { get; init; } = new();
    public ConfigField SessionDurationMinutes { get; init; } = new();
    public ConfigField TokenDefaultExpirationDays { get; init; } = new();

    public LocalPasscodeConfigSection Local { get; init; } = new();
    public OidcConfigSection Oidc { get; init; } = new();
    public RateLimitConfigSection RateLimit { get; init; } = new();
}

public sealed record LocalPasscodeConfigSection
{
    public ConfigField MinLength { get; init; } = new();
    public ConfigField MaxFailedAttempts { get; init; } = new();
    public ConfigField LockoutMinutes { get; init; } = new();
}

public sealed record OidcConfigSection
{
    public ConfigField Authority { get; init; } = new();
    public ConfigField ClientId { get; init; } = new();
    public ConfigField ClientSecret { get; init; } = new();
    public ConfigField CallbackPath { get; init; } = new();
    public ConfigField AdminSubjectClaim { get; init; } = new();
}

public sealed record RateLimitConfigSection
{
    public ConfigField LoginPerMinutePerIp { get; init; } = new();
    public ConfigField LoginPerHourPerIp { get; init; } = new();
    public ConfigField LoginPerMinuteGlobal { get; init; } = new();
    public ConfigField TokenCreationPerHour { get; init; } = new();
}

public sealed record RoutingConfigSection
{
    public string SectionLabel { get; } = "Intelligent Routing";
    public bool RestartRequired { get; } = false;

    public ConfigField Enabled { get; init; } = new();
    public ConfigField ShadowMode { get; init; } = new();
    public ConfigField EnableQualityEscalation { get; init; } = new();
    public ConfigField SmallMaxTokens { get; init; } = new();
    public ConfigField MediumMaxTokens { get; init; } = new();
    public ConfigField SmallAlias { get; init; } = new();
    public ConfigField MediumAlias { get; init; } = new();
    public ConfigField LargeAlias { get; init; } = new();
    public ConfigField CooldownSeconds { get; init; } = new();
    public ConfigField MaxProviderAttempts { get; init; } = new();
    public SpendGuardConfigSection SpendGuard { get; init; } = new();
}

public sealed record SpendGuardConfigSection
{
    public ConfigField DailyPaidRequestSoftLimit { get; init; } = new();
    public ConfigField DailyPaidRequestHardLimit { get; init; } = new();
}

public sealed record EngagementConfigSection
{
    public string SectionLabel { get; } = "Engagement Rules";
    public bool RestartRequired { get; } = false;

    /// <summary>
    /// Engagement rules are governed by the AGENTS.md file and runtime JSON.
    /// This section reflects the runtime-loaded state; the source of truth is the agent config path.
    /// </summary>
    public ConfigField SourceOfTruth { get; init; } = new();
}

public sealed record ChannelsConfigSection
{
    public string SectionLabel { get; } = "Channel Integrations";
    public bool RestartRequired { get; } = true;

    public ConfigField SignalPhoneNumber { get; init; } = new();
    public ConfigField SignalServerUrl { get; init; } = new();
    public ConfigField SignalApiToken { get; init; } = new();
    public ConfigField DiscordBotToken { get; init; } = new();
    public ConfigField DiscordChannelId { get; init; } = new();
}
