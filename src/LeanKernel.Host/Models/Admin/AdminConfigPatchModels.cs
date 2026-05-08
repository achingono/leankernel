namespace LeanKernel.Host.Models.Admin;

/// <summary>
/// Request body for PATCH /api/config.
/// Clients send only the sections they want to update; null sections are ignored.
/// Secrets shown as "***" or env-backed references are not writable through this endpoint.
/// </summary>
public sealed class AdminConfigPatchRequest
{
    public LiteLlmPatch? LiteLlm { get; set; }
    public QdrantPatch? Qdrant { get; set; }
    public SignalPatch? Signal { get; set; }
    public UnstructuredPatch? Unstructured { get; set; }
    public WikiPatch? Wiki { get; set; }
    public AgentsPatch? Agents { get; set; }
    public KnowledgePatch? Knowledge { get; set; }
    public ContextPatch? Context { get; set; }
    public SchedulerPatch? Scheduler { get; set; }
    public RoutingPatch? Routing { get; set; }
}

public sealed class LiteLlmPatch
{
    public string? BaseUrl { get; set; }
    public string? DefaultModel { get; set; }
    public string? EmbeddingModel { get; set; }
    public int? ContextWindowTokens { get; set; }
}

public sealed class QdrantPatch
{
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? CollectionName { get; set; }
    public int? EmbeddingDimension { get; set; }
}

public sealed class SignalPatch
{
    public string? CliPath { get; set; }
    public bool? Enabled { get; set; }
    public string[]? AllowedSenders { get; set; }
    public string? DaemonBaseUrl { get; set; }
}

public sealed class UnstructuredPatch
{
    public bool? Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public int? TimeoutSeconds { get; set; }
}

public sealed class WikiPatch
{
    public string? BasePath { get; set; }
    public int? MaxFactsPerEntry { get; set; }
    public int? StaleFactDays { get; set; }
    public double? MinConfidenceThreshold { get; set; }
}

public sealed class AgentsPatch
{
    public string? BasePath { get; set; }
}

public sealed class KnowledgePatch
{
    public bool? Enabled { get; set; }
    public string? CollectionName { get; set; }
    public string? DocumentsPath { get; set; }
    public string[]? DefaultDocumentTags { get; set; }
}

public sealed class ContextPatch
{
    public double? SemanticSimilarityWeight { get; set; }
    public double? RecencyDecayWeight { get; set; }
    public double? DimensionMatchWeight { get; set; }
    public double? InteractionFrequencyWeight { get; set; }
    public double? MinRelevanceThreshold { get; set; }
    public int? MaxConversationTurns { get; set; }
}

public sealed class SchedulerPatch
{
    public bool? Enabled { get; set; }
    public string? WikiMaintenanceCron { get; set; }
    public string? ChatFactScrubCron { get; set; }
}

public sealed class RoutingPatch
{
    public bool? Enabled { get; set; }
    public bool? ShadowMode { get; set; }
    public bool? EnableQualityEscalation { get; set; }
    public int? SmallMaxTokens { get; set; }
    public int? MediumMaxTokens { get; set; }
    public string? SmallAlias { get; set; }
    public string? MediumAlias { get; set; }
    public string? LargeAlias { get; set; }
    public int? CooldownSeconds { get; set; }
    public int? MaxProviderAttempts { get; set; }
    public int? DailyPaidRequestSoftLimit { get; set; }
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
    public string Section { get; init; } = "";
    public string Field { get; init; } = "";
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public bool RestartRequired { get; init; }
}
