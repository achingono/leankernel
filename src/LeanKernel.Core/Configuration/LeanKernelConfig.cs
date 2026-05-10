namespace LeanKernel.Core.Configuration;

/// <summary>
/// Root configuration model, bound from appsettings.json under "LeanKernel" key.
/// </summary>
public sealed class LeanKernelConfig
{
    public const string SectionName = "LeanKernel";

    public LiteLlmConfig LiteLlm { get; set; } = new();
    public OllamaConfig Ollama { get; set; } = new();
    public QdrantConfig Qdrant { get; set; } = new();
    public SignalConfig Signal { get; set; } = new();
    public UnstructuredConfig Unstructured { get; set; } = new();
    public WikiConfig Wiki { get; set; } = new();
    public AgentsConfig Agents { get; set; } = new();
    public KnowledgeConfig Knowledge { get; set; } = new();
    public ContextConfig Context { get; set; } = new();
    public SchedulerConfig Scheduler { get; set; } = new();
    public AuthConfig Auth { get; set; } = new();
    public RoutingConfig Routing { get; set; } = new();
    public EngagementRules Engagement { get; set; } = new();
    /// <summary>
    /// Gets or sets options for the post-turn self-improvement pipeline.
    /// </summary>
    public SelfImprovementConfig SelfImprovement { get; set; } = new();

    /// <summary>
    /// Gets or sets the Signal account phone number used by channel adapters.
    /// </summary>
    public string? SignalPhoneNumber { get; set; }

    /// <summary>
    /// Gets or sets the Signal daemon base URL.
    /// </summary>
    public string? SignalServerUrl { get; set; }

    /// <summary>
    /// Gets or sets the optional API token for Signal daemon calls.
    /// </summary>
    public string? SignalApiToken { get; set; }

    /// <summary>
    /// Gets or sets the Discord bot token.
    /// </summary>
    public string? DiscordBotToken { get; set; }

    /// <summary>
    /// Gets or sets the Discord channel identifier used for outbound delivery.
    /// </summary>
    public string? DiscordChannelId { get; set; }
}

/// <summary>
/// Configuration for durable post-turn learning.
/// </summary>
public sealed class SelfImprovementConfig
{
    /// <summary>
    /// Gets or sets whether the self-improvement pipeline is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the directory used for durable queued turn events.
    /// </summary>
    public string QueuePath { get; set; } = "queue/learning";

    /// <summary>
    /// Gets or sets whether deterministic regex-based fact extraction runs.
    /// </summary>
    public bool RegexExtractionEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether semantic LLM-based fact extraction runs.
    /// </summary>
    public bool LlmExtractionEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether identity files are refreshed from completed turns.
    /// </summary>
    public bool IdentityRefreshEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether failed turns are classified for recovery and capability-gap learning.
    /// </summary>
    public bool FailureRecoveryEnabled { get; set; } = true;
}

public enum AuthMode { LocalPasscode, Oidc, Disabled }

public sealed class AuthConfig
{
    public AuthMode Mode { get; set; } = AuthMode.LocalPasscode;
    public int SessionDurationMinutes { get; set; } = 480;
    public int TokenDefaultExpirationDays { get; set; } = 90;
    public string[] AllowedOrigins { get; set; } = [];
    public LocalPasscodeConfig Local { get; set; } = new();
    public OidcConfig Oidc { get; set; } = new();
    public RateLimitConfig RateLimit { get; set; } = new();
}

public sealed class LocalPasscodeConfig
{
    public int MinLength { get; set; } = 8;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public sealed class OidcConfig
{
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CallbackPath { get; set; } = "/auth/oidc/callback";
    public string[] Scopes { get; set; } = ["openid", "profile", "email"];
    public string AdminSubjectClaim { get; set; } = "";
    public string AdminClaimType { get; set; } = "sub";
}

public sealed class RateLimitConfig
{
    public int LoginPerMinutePerIp { get; set; } = 5;
    public int LoginPerHourPerIp { get; set; } = 20;
    public int LoginPerMinuteGlobal { get; set; } = 50;
    public int TokenCreationPerHour { get; set; } = 10;
}

public sealed class LiteLlmConfig
{
    public string BaseUrl { get; set; } = "http://litellm:4000";
    public string ApiKey { get; set; } = "sk-LeanKernel-local";
    public string DefaultModel { get; set; } = "small";
    public string EmbeddingModel { get; set; } = "embedding-small";
    public int ContextWindowTokens { get; set; } = 128_000;
}

public sealed class OllamaConfig
{
    /// <summary>
    /// Base URL for local Ollama instance (e.g., "http://host.docker.internal:11434" or "http://ollama:11434").
    /// </summary>
    public string BaseUrl { get; set; } = "http://host.docker.internal:11434";

    /// <summary>
    /// Model name to use for semantic extraction (e.g., "mistral", "neural-chat", "llama2").
    /// Must be available in the local Ollama instance.
    /// </summary>
    public string Model { get; set; } = "mistral";

    /// <summary>
    /// Temperature for extraction (0-1, lower = more deterministic).
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// HTTP timeout in seconds for Ollama requests.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class QdrantConfig
{
    public string Host { get; set; } = "qdrant";
    public int Port { get; set; } = 6334;
    /// <summary>
    /// Legacy collection name. Use Knowledge.CollectionName for the unified knowledge collection.
    /// Retained for backward compatibility with existing Qdrant data.
    /// </summary>
    public string CollectionName { get; set; } = "LEANKERNEL_knowledge";
    public int EmbeddingDimension { get; set; } = 1536;
}

public sealed class SignalConfig
{
    public string CliPath { get; set; } = "/usr/bin/signal-cli";
    public string Account { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string[] AllowedSenders { get; set; } = [];

    /// <summary>
    /// Base URL of the signal-daemon HTTP sidecar (e.g. "http://LeanKernel-signal:8080").
    /// When set, <see cref="SignalRestApiAdapter"/> is used instead of the local
    /// signal-cli child process, which eliminates config-lock contention.
    /// </summary>
    public string DaemonBaseUrl { get; set; } = "";
}

public sealed class UnstructuredConfig
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://unstructured:8000";
    public int TimeoutSeconds { get; set; } = 120;
    public string[] SupportedMimeTypes { get; set; } =
    [
        "application/pdf",
        "application/rtf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/epub+zip",
        "message/rfc822",
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/gif",
        "image/bmp",
        "image/tiff"
    ];

    public string[] SupportedExtensions { get; set; } =
    [
        ".pdf",
        ".doc",
        ".docx",
        ".ppt",
        ".pptx",
        ".xls",
        ".xlsx",
        ".rtf",
        ".odt",
        ".epub",
        ".eml",
        ".msg",
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff"
    ];
}

public sealed class AgentsConfig
{
    public string BasePath { get; set; } = "/app/data/agents";
}

public sealed class WikiConfig
{
    public string BasePath { get; set; } = "/app/data/wiki";
    public int MaxFactsPerEntry { get; set; } = 20;
    public int StaleFactDays { get; set; } = 30;
    public double MinConfidenceThreshold { get; set; } = 0.5;
}

public sealed class ContextConfig
{
    public double SemanticSimilarityWeight { get; set; } = 0.40;
    public double RecencyDecayWeight { get; set; } = 0.20;
    public double DimensionMatchWeight { get; set; } = 0.25;
    public double InteractionFrequencyWeight { get; set; } = 0.15;
    public double MinRelevanceThreshold { get; set; } = 0.65;
    public int MaxConversationTurns { get; set; } = 15;
}

public sealed class KnowledgeConfig
{
    public bool Enabled { get; set; } = true;
    public string CollectionName { get; set; } = "LEANKERNEL_knowledge";
    public int EmbeddingDimension { get; set; } = 1536;
    public string DocumentsPath { get; set; } = "/app/data/agents/main/documents";
    public string[] DefaultDocumentTags { get; set; } = ["general"];
    public Dictionary<string, AgentScopeConfig> AgentScopes { get; set; } = new();
    public List<TagRule> TagRules { get; set; } = [];
}

public sealed class AgentScopeConfig
{
    public string[] Tags { get; set; } = [];
    public string Description { get; set; } = "";
}

public sealed class TagRule
{
    public string PathPattern { get; set; } = "";
    public string[] Tags { get; set; } = [];
}

public sealed class SchedulerConfig
{
    public bool Enabled { get; set; } = true;
    public string WikiMaintenanceCron { get; set; } = "0 3 * * *"; // 3 AM daily
    public string ChatFactScrubCron { get; set; } = "30 2 * * *"; // 2:30 AM daily
    public string UserProfileSyncCron { get; set; } = "0 4 * * *"; // 4 AM daily
}

public sealed class RoutingConfig
{
    /// <summary>
    /// When true, the intelligent routing pipeline is active.
    /// When false, the static default model is used for all requests.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Shadow mode runs routing in parallel but discards its response.
    /// Only the SelectionLog is emitted; the static response is returned to the user.
    /// Requires Enabled=true.
    /// </summary>
    public bool ShadowMode { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the quality gate can trigger escalation to another model candidate.
    /// </summary>
    public bool EnableQualityEscalation { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum prompt tokens for a request to be considered small.
    /// </summary>
    public int SmallMaxTokens { get; set; } = 4_000;

    /// <summary>
    /// Gets or sets the maximum detected constraints for a request to be considered small.
    /// </summary>
    public int SmallMaxConstraints { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum prompt tokens for a request to be considered medium.
    /// </summary>
    public int MediumMaxTokens { get; set; } = 16_000;

    /// <summary>
    /// Gets or sets the maximum detected constraints for a request to be considered medium.
    /// </summary>
    public int MediumMaxConstraints { get; set; } = 8;

    /// <summary>
    /// Gets or sets the LiteLLM alias used for small requests.
    /// </summary>
    public string SmallAlias { get; set; } = "small";

    /// <summary>
    /// Gets or sets the LiteLLM alias used for medium requests.
    /// </summary>
    public string MediumAlias { get; set; } = "medium";

    /// <summary>
    /// Gets or sets the LiteLLM alias used for large requests.
    /// </summary>
    public string LargeAlias { get; set; } = "large";

    /// <summary>
    /// Gets or sets how long unhealthy providers stay in cooldown.
    /// </summary>
    public int CooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the maximum model/provider attempts for one routed turn.
    /// </summary>
    public int MaxProviderAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum time allowed for selecting and invoking a model candidate.
    /// </summary>
    public int MaxSelectionBudgetMs { get; set; } = 30_000;

    /// <summary>
    /// Gets or sets the minimum output length used by the response quality gate.
    /// </summary>
    public int QualityMinOutputLength { get; set; } = 80;

    /// <summary>
    /// Gets or sets the minimum constraint coverage required by the response quality gate.
    /// </summary>
    public double QualityMinConstraintCoverage { get; set; } = 0.80;

    /// <summary>
    /// Gets or sets spend-guard thresholds for paid fallback usage.
    /// </summary>
    public SpendGuardConfig SpendGuard { get; set; } = new();

    /// <summary>
    /// Gets or sets the cron expression for synchronizing model limits from provider metadata.
    /// </summary>
    public string ModelLimitSyncCron { get; set; } = "0 4 * * *"; // 4 AM daily
}

public sealed class SpendGuardConfig
{
    /// <summary>Daily paid-request soft threshold (warning alert). 0 = disabled.</summary>
    public int DailyPaidRequestSoftLimit { get; set; } = 0;

    /// <summary>Daily paid-request hard threshold (disables paid fallback). 0 = disabled.</summary>
    public int DailyPaidRequestHardLimit { get; set; } = 0;
}
