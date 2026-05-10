using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Models.Admin;

/// <summary>
/// Top-level response for the admin config API.
/// Covers every supported <see cref="LeanKernelConfig"/> section with provenance metadata.
/// </summary>
public sealed record AdminConfigResponse
{
    /// <summary>
    /// Gets or sets the lite llm.
    /// </summary>
    public LiteLlmConfigSection LiteLlm { get; init; } = new();
    /// <summary>
    /// Gets or sets the qdrant.
    /// </summary>
    public QdrantConfigSection Qdrant { get; init; } = new();
    /// <summary>
    /// Gets or sets the signal.
    /// </summary>
    public SignalConfigSection Signal { get; init; } = new();
    /// <summary>
    /// Gets or sets the unstructured.
    /// </summary>
    public UnstructuredConfigSection Unstructured { get; init; } = new();
    /// <summary>
    /// Gets or sets the wiki.
    /// </summary>
    public WikiConfigSection Wiki { get; init; } = new();
    /// <summary>
    /// Gets or sets the agents.
    /// </summary>
    public AgentsConfigSection Agents { get; init; } = new();
    /// <summary>
    /// Gets or sets the knowledge.
    /// </summary>
    public KnowledgeConfigSection Knowledge { get; init; } = new();
    /// <summary>
    /// Gets or sets the context.
    /// </summary>
    public ContextConfigSection Context { get; init; } = new();
    /// <summary>
    /// Gets or sets the scheduler.
    /// </summary>
    public SchedulerConfigSection Scheduler { get; init; } = new();
    /// <summary>
    /// Gets or sets the auth.
    /// </summary>
    public AuthConfigSection Auth { get; init; } = new();
    /// <summary>
    /// Gets or sets the routing.
    /// </summary>
    public RoutingConfigSection Routing { get; init; } = new();
    /// <summary>
    /// Gets or sets the engagement.
    /// </summary>
    public EngagementConfigSection Engagement { get; init; } = new();
    /// <summary>
    /// Gets or sets the channels.
    /// </summary>
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

/// <summary>
/// Represents the lite llm config section.
/// </summary>
public sealed record LiteLlmConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "LiteLLM Connectivity";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = false;

    /// <summary>
    /// Gets or sets the base url.
    /// </summary>
    public ConfigField BaseUrl { get; init; } = new();
    /// <summary>
    /// Gets or sets the api key.
    /// </summary>
    public ConfigField ApiKey { get; init; } = new();
    /// <summary>
    /// Gets or sets the default model.
    /// </summary>
    public ConfigField DefaultModel { get; init; } = new();
    /// <summary>
    /// Gets or sets the embedding model.
    /// </summary>
    public ConfigField EmbeddingModel { get; init; } = new();
    /// <summary>
    /// Gets or sets the context window tokens.
    /// </summary>
    public ConfigField ContextWindowTokens { get; init; } = new();
}

/// <summary>
/// Represents the qdrant config section.
/// </summary>
public sealed record QdrantConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Qdrant Vector Store";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = true;

    /// <summary>
    /// Gets or sets the host.
    /// </summary>
    public ConfigField Host { get; init; } = new();
    /// <summary>
    /// Gets or sets the port.
    /// </summary>
    public ConfigField Port { get; init; } = new();
    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
    public ConfigField CollectionName { get; init; } = new();
    /// <summary>
    /// Gets or sets the embedding dimension.
    /// </summary>
    public ConfigField EmbeddingDimension { get; init; } = new();
}

/// <summary>
/// Represents the signal config section.
/// </summary>
public sealed record SignalConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Signal Integration";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = false;

    /// <summary>
    /// Gets or sets the cli path.
    /// </summary>
    public ConfigField CliPath { get; init; } = new();
    /// <summary>
    /// Gets or sets the account.
    /// </summary>
    public ConfigField Account { get; init; } = new();
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public ConfigField Enabled { get; init; } = new();
    /// <summary>
    /// Gets or sets the allowed senders.
    /// </summary>
    public ConfigField AllowedSenders { get; init; } = new();
    /// <summary>
    /// Gets or sets the daemon base url.
    /// </summary>
    public ConfigField DaemonBaseUrl { get; init; } = new();
}

/// <summary>
/// Represents the unstructured config section.
/// </summary>
public sealed record UnstructuredConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Unstructured Document Processing";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = false;

    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public ConfigField Enabled { get; init; } = new();
    /// <summary>
    /// Gets or sets the base url.
    /// </summary>
    public ConfigField BaseUrl { get; init; } = new();
    /// <summary>
    /// Gets or sets the timeout seconds.
    /// </summary>
    public ConfigField TimeoutSeconds { get; init; } = new();
    /// <summary>
    /// Gets or sets the supported mime types.
    /// </summary>
    public ConfigField SupportedMimeTypes { get; init; } = new();
    /// <summary>
    /// Gets or sets the supported extensions.
    /// </summary>
    public ConfigField SupportedExtensions { get; init; } = new();
}

/// <summary>
/// Represents the wiki config section.
/// </summary>
public sealed record WikiConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Wiki Storage";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = false;

    /// <summary>
    /// Gets or sets the base path.
    /// </summary>
    public ConfigField BasePath { get; init; } = new();
    /// <summary>
    /// Gets or sets the max facts per entry.
    /// </summary>
    public ConfigField MaxFactsPerEntry { get; init; } = new();
    /// <summary>
    /// Gets or sets the stale fact days.
    /// </summary>
    public ConfigField StaleFactDays { get; init; } = new();
    /// <summary>
    /// Gets or sets the min confidence threshold.
    /// </summary>
    public ConfigField MinConfidenceThreshold { get; init; } = new();
}

/// <summary>
/// Represents the agents config section.
/// </summary>
public sealed record AgentsConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Agent Behavior";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = false;

    /// <summary>
    /// Gets or sets the base path.
    /// </summary>
    public ConfigField BasePath { get; init; } = new();
}

/// <summary>
/// Represents the knowledge config section.
/// </summary>
public sealed record KnowledgeConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Knowledge Retrieval";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = false;

    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public ConfigField Enabled { get; init; } = new();
    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
    public ConfigField CollectionName { get; init; } = new();
    /// <summary>
    /// Gets or sets the embedding dimension.
    /// </summary>
    public ConfigField EmbeddingDimension { get; init; } = new();
    /// <summary>
    /// Gets or sets the documents path.
    /// </summary>
    public ConfigField DocumentsPath { get; init; } = new();
    /// <summary>
    /// Gets or sets the default document tags.
    /// </summary>
    public ConfigField DefaultDocumentTags { get; init; } = new();
}

/// <summary>
/// Represents the context config section.
/// </summary>
public sealed record ContextConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Context Retrieval Weights";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = false;

    /// <summary>
    /// Gets or sets the semantic similarity weight.
    /// </summary>
    public ConfigField SemanticSimilarityWeight { get; init; } = new();
    /// <summary>
    /// Gets or sets the recency decay weight.
    /// </summary>
    public ConfigField RecencyDecayWeight { get; init; } = new();
    /// <summary>
    /// Gets or sets the dimension match weight.
    /// </summary>
    public ConfigField DimensionMatchWeight { get; init; } = new();
    /// <summary>
    /// Gets or sets the interaction frequency weight.
    /// </summary>
    public ConfigField InteractionFrequencyWeight { get; init; } = new();
    /// <summary>
    /// Gets or sets the min relevance threshold.
    /// </summary>
    public ConfigField MinRelevanceThreshold { get; init; } = new();
    /// <summary>
    /// Gets or sets the max conversation turns.
    /// </summary>
    public ConfigField MaxConversationTurns { get; init; } = new();
}

/// <summary>
/// Represents the scheduler config section.
/// </summary>
public sealed record SchedulerConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Scheduler";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = false;

    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public ConfigField Enabled { get; init; } = new();
    /// <summary>
    /// Gets or sets the wiki maintenance cron.
    /// </summary>
    public ConfigField WikiMaintenanceCron { get; init; } = new();
    /// <summary>
    /// Gets or sets the chat fact scrub cron.
    /// </summary>
    public ConfigField ChatFactScrubCron { get; init; } = new();
}

/// <summary>
/// Represents the auth config section.
/// </summary>
public sealed record AuthConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Authentication";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = true;

    /// <summary>
    /// Gets or sets the mode.
    /// </summary>
    public ConfigField Mode { get; init; } = new();
    /// <summary>
    /// Gets or sets the session duration minutes.
    /// </summary>
    public ConfigField SessionDurationMinutes { get; init; } = new();
    /// <summary>
    /// Gets or sets the token default expiration days.
    /// </summary>
    public ConfigField TokenDefaultExpirationDays { get; init; } = new();

    /// <summary>
    /// Gets or sets the local.
    /// </summary>
    public LocalPasscodeConfigSection Local { get; init; } = new();
    /// <summary>
    /// Gets or sets the oidc.
    /// </summary>
    public OidcConfigSection Oidc { get; init; } = new();
    /// <summary>
    /// Gets or sets the rate limit.
    /// </summary>
    public RateLimitConfigSection RateLimit { get; init; } = new();
}

/// <summary>
/// Represents the local passcode config section.
/// </summary>
public sealed record LocalPasscodeConfigSection
{
    /// <summary>
    /// Gets or sets the min length.
    /// </summary>
    public ConfigField MinLength { get; init; } = new();
    /// <summary>
    /// Gets or sets the max failed attempts.
    /// </summary>
    public ConfigField MaxFailedAttempts { get; init; } = new();
    /// <summary>
    /// Gets or sets the lockout minutes.
    /// </summary>
    public ConfigField LockoutMinutes { get; init; } = new();
}

/// <summary>
/// Represents the oidc config section.
/// </summary>
public sealed record OidcConfigSection
{
    /// <summary>
    /// Gets or sets the authority.
    /// </summary>
    public ConfigField Authority { get; init; } = new();
    /// <summary>
    /// Gets or sets the client id.
    /// </summary>
    public ConfigField ClientId { get; init; } = new();
    /// <summary>
    /// Gets or sets the client secret.
    /// </summary>
    public ConfigField ClientSecret { get; init; } = new();
    /// <summary>
    /// Gets or sets the callback path.
    /// </summary>
    public ConfigField CallbackPath { get; init; } = new();
    /// <summary>
    /// Gets or sets the admin subject claim.
    /// </summary>
    public ConfigField AdminSubjectClaim { get; init; } = new();
}

/// <summary>
/// Represents the rate limit config section.
/// </summary>
public sealed record RateLimitConfigSection
{
    /// <summary>
    /// Gets or sets the login per minute per ip.
    /// </summary>
    public ConfigField LoginPerMinutePerIp { get; init; } = new();
    /// <summary>
    /// Gets or sets the login per hour per ip.
    /// </summary>
    public ConfigField LoginPerHourPerIp { get; init; } = new();
    /// <summary>
    /// Gets or sets the login per minute global.
    /// </summary>
    public ConfigField LoginPerMinuteGlobal { get; init; } = new();
    /// <summary>
    /// Gets or sets the token creation per hour.
    /// </summary>
    public ConfigField TokenCreationPerHour { get; init; } = new();
}

/// <summary>
/// Represents the routing config section.
/// </summary>
public sealed record RoutingConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Intelligent Routing";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = false;

    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public ConfigField Enabled { get; init; } = new();
    /// <summary>
    /// Gets or sets the shadow mode.
    /// </summary>
    public ConfigField ShadowMode { get; init; } = new();
    /// <summary>
    /// Gets or sets the enable quality escalation.
    /// </summary>
    public ConfigField EnableQualityEscalation { get; init; } = new();
    /// <summary>
    /// Gets or sets the small max tokens.
    /// </summary>
    public ConfigField SmallMaxTokens { get; init; } = new();
    /// <summary>
    /// Gets or sets the medium max tokens.
    /// </summary>
    public ConfigField MediumMaxTokens { get; init; } = new();
    /// <summary>
    /// Gets or sets the small alias.
    /// </summary>
    public ConfigField SmallAlias { get; init; } = new();
    /// <summary>
    /// Gets or sets the medium alias.
    /// </summary>
    public ConfigField MediumAlias { get; init; } = new();
    /// <summary>
    /// Gets or sets the large alias.
    /// </summary>
    public ConfigField LargeAlias { get; init; } = new();
    /// <summary>
    /// Gets or sets the cooldown seconds.
    /// </summary>
    public ConfigField CooldownSeconds { get; init; } = new();
    /// <summary>
    /// Gets or sets the max provider attempts.
    /// </summary>
    public ConfigField MaxProviderAttempts { get; init; } = new();
    /// <summary>
    /// Gets or sets the spend guard.
    /// </summary>
    public SpendGuardConfigSection SpendGuard { get; init; } = new();
}

/// <summary>
/// Represents the spend guard config section.
/// </summary>
public sealed record SpendGuardConfigSection
{
    /// <summary>
    /// Gets or sets the daily paid request soft limit.
    /// </summary>
    public ConfigField DailyPaidRequestSoftLimit { get; init; } = new();
    /// <summary>
    /// Gets or sets the daily paid request hard limit.
    /// </summary>
    public ConfigField DailyPaidRequestHardLimit { get; init; } = new();
}

/// <summary>
/// Represents the engagement config section.
/// </summary>
public sealed record EngagementConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Engagement Rules";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = false;

    /// <summary>
    /// Engagement rules are governed by the AGENTS.md file and runtime JSON.
    /// This section reflects the runtime-loaded state; the source of truth is the agent config path.
    /// </summary>
    public ConfigField SourceOfTruth { get; init; } = new();
}

/// <summary>
/// Represents the channels config section.
/// </summary>
public sealed record ChannelsConfigSection
{
    /// <summary>
    /// Gets or sets the section label.
    /// </summary>
    public string SectionLabel { get; } = "Channel Integrations";
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; } = true;

    /// <summary>
    /// Gets or sets the signal phone number.
    /// </summary>
    public ConfigField SignalPhoneNumber { get; init; } = new();
    /// <summary>
    /// Gets or sets the signal server url.
    /// </summary>
    public ConfigField SignalServerUrl { get; init; } = new();
    /// <summary>
    /// Gets or sets the signal api token.
    /// </summary>
    public ConfigField SignalApiToken { get; init; } = new();
    /// <summary>
    /// Gets or sets the discord bot token.
    /// </summary>
    public ConfigField DiscordBotToken { get; init; } = new();
    /// <summary>
    /// Gets or sets the discord channel id.
    /// </summary>
    public ConfigField DiscordChannelId { get; init; } = new();
}
