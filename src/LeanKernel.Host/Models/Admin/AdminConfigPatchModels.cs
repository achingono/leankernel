namespace LeanKernel.Host.Models.Admin;

/// <summary>
/// Request body for PATCH /api/config.
/// Clients send only the sections they want to update; null sections are ignored.
/// Secrets shown as "***" or env-backed references are not writable through this endpoint.
/// </summary>
public sealed class AdminConfigPatchRequest
{
    /// <summary>
    /// Gets or sets the lite llm.
    /// </summary>
    public LiteLlmPatch? LiteLlm { get; set; }
    /// <summary>
    /// Gets or sets the qdrant.
    /// </summary>
    public QdrantPatch? Qdrant { get; set; }
    /// <summary>
    /// Gets or sets the signal.
    /// </summary>
    public SignalPatch? Signal { get; set; }
    /// <summary>
    /// Gets or sets the unstructured.
    /// </summary>
    public UnstructuredPatch? Unstructured { get; set; }
    /// <summary>
    /// Gets or sets the wiki.
    /// </summary>
    public WikiPatch? Wiki { get; set; }
    /// <summary>
    /// Gets or sets the agents.
    /// </summary>
    public AgentsPatch? Agents { get; set; }
    /// <summary>
    /// Gets or sets the knowledge.
    /// </summary>
    public KnowledgePatch? Knowledge { get; set; }
    /// <summary>
    /// Gets or sets the context.
    /// </summary>
    public ContextPatch? Context { get; set; }
    /// <summary>
    /// Gets or sets the scheduler.
    /// </summary>
    public SchedulerPatch? Scheduler { get; set; }
    /// <summary>
    /// Gets or sets the routing.
    /// </summary>
    public RoutingPatch? Routing { get; set; }
}

/// <summary>
/// Represents the lite llm patch.
/// </summary>
public sealed class LiteLlmPatch
{
    /// <summary>
    /// Gets or sets the base url.
    /// </summary>
    public string? BaseUrl { get; set; }
    /// <summary>
    /// Gets or sets the default model.
    /// </summary>
    public string? DefaultModel { get; set; }
    /// <summary>
    /// Gets or sets the embedding model.
    /// </summary>
    public string? EmbeddingModel { get; set; }
    /// <summary>
    /// Gets or sets the context window tokens.
    /// </summary>
    public int? ContextWindowTokens { get; set; }
}

/// <summary>
/// Represents the qdrant patch.
/// </summary>
public sealed class QdrantPatch
{
    /// <summary>
    /// Gets or sets the host.
    /// </summary>
    public string? Host { get; set; }
    /// <summary>
    /// Gets or sets the port.
    /// </summary>
    public int? Port { get; set; }
    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
    public string? CollectionName { get; set; }
    /// <summary>
    /// Gets or sets the embedding dimension.
    /// </summary>
    public int? EmbeddingDimension { get; set; }
}

/// <summary>
/// Represents the signal patch.
/// </summary>
public sealed class SignalPatch
{
    /// <summary>
    /// Gets or sets the cli path.
    /// </summary>
    public string? CliPath { get; set; }
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool? Enabled { get; set; }
    /// <summary>
    /// Gets or sets the allowed senders.
    /// </summary>
    public string[]? AllowedSenders { get; set; }
    /// <summary>
    /// Gets or sets the daemon base url.
    /// </summary>
    public string? DaemonBaseUrl { get; set; }
}

/// <summary>
/// Represents the unstructured patch.
/// </summary>
public sealed class UnstructuredPatch
{
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool? Enabled { get; set; }
    /// <summary>
    /// Gets or sets the base url.
    /// </summary>
    public string? BaseUrl { get; set; }
    /// <summary>
    /// Gets or sets the timeout seconds.
    /// </summary>
    public int? TimeoutSeconds { get; set; }
    /// <summary>
    /// Gets or sets the supported mime types.
    /// </summary>
    public string[]? SupportedMimeTypes { get; set; }
    /// <summary>
    /// Gets or sets the supported extensions.
    /// </summary>
    public string[]? SupportedExtensions { get; set; }
}

/// <summary>
/// Represents the wiki patch.
/// </summary>
public sealed class WikiPatch
{
    /// <summary>
    /// Gets or sets the base path.
    /// </summary>
    public string? BasePath { get; set; }
    /// <summary>
    /// Gets or sets the max facts per entry.
    /// </summary>
    public int? MaxFactsPerEntry { get; set; }
    /// <summary>
    /// Gets or sets the stale fact days.
    /// </summary>
    public int? StaleFactDays { get; set; }
    /// <summary>
    /// Gets or sets the min confidence threshold.
    /// </summary>
    public double? MinConfidenceThreshold { get; set; }
}

/// <summary>
/// Represents the agents patch.
/// </summary>
public sealed class AgentsPatch
{
    /// <summary>
    /// Gets or sets the base path.
    /// </summary>
    public string? BasePath { get; set; }
}

/// <summary>
/// Represents the knowledge patch.
/// </summary>
public sealed class KnowledgePatch
{
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool? Enabled { get; set; }
    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
    public string? CollectionName { get; set; }
    /// <summary>
    /// Gets or sets the documents path.
    /// </summary>
    public string? DocumentsPath { get; set; }
    /// <summary>
    /// Gets or sets the default document tags.
    /// </summary>
    public string[]? DefaultDocumentTags { get; set; }
}

/// <summary>
/// Represents the context patch.
/// </summary>
public sealed class ContextPatch
{
    /// <summary>
    /// Gets or sets the semantic similarity weight.
    /// </summary>
    public double? SemanticSimilarityWeight { get; set; }
    /// <summary>
    /// Gets or sets the recency decay weight.
    /// </summary>
    public double? RecencyDecayWeight { get; set; }
    /// <summary>
    /// Gets or sets the dimension match weight.
    /// </summary>
    public double? DimensionMatchWeight { get; set; }
    /// <summary>
    /// Gets or sets the interaction frequency weight.
    /// </summary>
    public double? InteractionFrequencyWeight { get; set; }
    /// <summary>
    /// Gets or sets the min relevance threshold.
    /// </summary>
    public double? MinRelevanceThreshold { get; set; }
    /// <summary>
    /// Gets or sets the max conversation turns.
    /// </summary>
    public int? MaxConversationTurns { get; set; }
    /// <summary>
    /// Gets or sets the entity subject boost.
    /// </summary>
    public double? EntitySubjectBoost { get; set; }
    /// <summary>
    /// Gets or sets the supporting entity threshold.
    /// </summary>
    public double? SupportingEntityThreshold { get; set; }
    /// <summary>
    /// Gets or sets the entity expansion depth.
    /// </summary>
    public int? EntityExpansionDepth { get; set; }
    /// <summary>
    /// Gets or sets the low-confidence fallback threshold.
    /// </summary>
    public double? LowConfidenceFallbackThreshold { get; set; }
    /// <summary>
    /// Gets or sets deprioritized recall max results.
    /// </summary>
    public int? DeprioritizedRecallMaxResults { get; set; }
    /// <summary>
    /// Gets or sets ambiguity low-confidence threshold.
    /// </summary>
    public double? AmbiguityLowConfidenceThreshold { get; set; }
    /// <summary>
    /// Gets or sets ambiguity confidence gap threshold.
    /// </summary>
    public double? AmbiguityConfidenceGapThreshold { get; set; }
}

/// <summary>
/// Represents the scheduler patch.
/// </summary>
public sealed class SchedulerPatch
{
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool? Enabled { get; set; }
    /// <summary>
    /// Gets or sets the wiki maintenance cron.
    /// </summary>
    public string? WikiMaintenanceCron { get; set; }
    /// <summary>
    /// Gets or sets the chat fact scrub cron.
    /// </summary>
    public string? ChatFactScrubCron { get; set; }
}

/// <summary>
/// Represents the routing patch.
/// </summary>
public sealed class RoutingPatch
{
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool? Enabled { get; set; }
    /// <summary>
    /// Gets or sets the shadow mode.
    /// </summary>
    public bool? ShadowMode { get; set; }
    /// <summary>
    /// Gets or sets the enable quality escalation.
    /// </summary>
    public bool? EnableQualityEscalation { get; set; }
    /// <summary>
    /// Gets or sets the small max tokens.
    /// </summary>
    public int? SmallMaxTokens { get; set; }
    /// <summary>
    /// Gets or sets the medium max tokens.
    /// </summary>
    public int? MediumMaxTokens { get; set; }
    /// <summary>
    /// Gets or sets the small alias.
    /// </summary>
    public string? SmallAlias { get; set; }
    /// <summary>
    /// Gets or sets the medium alias.
    /// </summary>
    public string? MediumAlias { get; set; }
    /// <summary>
    /// Gets or sets the large alias.
    /// </summary>
    public string? LargeAlias { get; set; }
    /// <summary>
    /// Gets or sets the cooldown seconds.
    /// </summary>
    public int? CooldownSeconds { get; set; }
    /// <summary>
    /// Gets or sets the max provider attempts.
    /// </summary>
    public int? MaxProviderAttempts { get; set; }
    /// <summary>
    /// Gets or sets the daily paid request soft limit.
    /// </summary>
    public int? DailyPaidRequestSoftLimit { get; set; }
    /// <summary>
    /// Gets or sets the daily paid request hard limit.
    /// </summary>
    public int? DailyPaidRequestHardLimit { get; set; }
}

/// <summary>
/// Response for PATCH /api/config — contains the applied diff and updated config.
/// </summary>
public sealed class AdminConfigPatchResponse
{
    /// <summary>List of human-readable changes that were applied.</summary>
    public List<ConfigChange> Changes { get; init; } = [];

    /// <summary>The full updated config metadata (same shape as GET /api/config).</summary>
    public AdminConfigResponse? UpdatedConfig { get; init; }
}

/// <summary>A single field change applied during a PATCH operation.</summary>
public sealed class ConfigChange
{
    /// <summary>
    /// Gets or sets the section.
    /// </summary>
    public string Section { get; init; } = "";
    /// <summary>
    /// Gets or sets the field.
    /// </summary>
    public string Field { get; init; } = "";
    /// <summary>
    /// Gets or sets the old value.
    /// </summary>
    public string? OldValue { get; init; }
    /// <summary>
    /// Gets or sets the new value.
    /// </summary>
    public string? NewValue { get; init; }
    /// <summary>
    /// Gets or sets the restart required.
    /// </summary>
    public bool RestartRequired { get; init; }
}
