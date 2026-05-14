namespace LeanKernel.Core.Configuration;

/// <summary>
/// Strategies for extracting facts from OpenClaw wiki pages.
/// </summary>
public enum WikiExtractionStrategy
{
    /// <summary>
    /// Deterministic regex/parsing-based extraction (fast, rule-based).
    /// </summary>
    Deterministic = 0,

    /// <summary>
    /// LLM-based extraction via Ollama (semantic, flexible, slower).
    /// </summary>
    LLM = 1
}

/// <summary>
/// Root configuration model, bound from appsettings.json under "LeanKernel" key.
/// </summary>
public sealed class LeanKernelConfig
{
    /// <summary>
    /// Represents the section name.
    /// </summary>
    public const string SectionName = "LeanKernel";

    /// <summary>
    /// Gets or sets the lite llm.
    /// </summary>
    public LiteLlmConfig LiteLlm { get; set; } = new();
    /// <summary>
    /// Gets or sets the ollama.
    /// </summary>
    public OllamaConfig Ollama { get; set; } = new();
    /// <summary>
    /// Gets or sets the qdrant.
    /// </summary>
    public QdrantConfig Qdrant { get; set; } = new();
    /// <summary>
    /// Gets or sets the signal.
    /// </summary>
    public SignalConfig Signal { get; set; } = new();
    /// <summary>
    /// Gets or sets the unstructured.
    /// </summary>
    public UnstructuredConfig Unstructured { get; set; } = new();
    /// <summary>
    /// Gets or sets the wiki.
    /// </summary>
    public WikiConfig Wiki { get; set; } = new();
    /// <summary>
    /// Gets or sets the agents.
    /// </summary>
    public AgentsConfig Agents { get; set; } = new();
    /// <summary>
    /// Gets or sets the knowledge.
    /// </summary>
    public KnowledgeConfig Knowledge { get; set; } = new();
    /// <summary>
    /// Gets or sets the context.
    /// </summary>
    public ContextConfig Context { get; set; } = new();
    /// <summary>
    /// Gets or sets the scheduler.
    /// </summary>
    public SchedulerConfig Scheduler { get; set; } = new();
    /// <summary>
    /// Gets or sets the auth.
    /// </summary>
    public AuthConfig Auth { get; set; } = new();
    /// <summary>
    /// Gets or sets the routing.
    /// </summary>
    public RoutingConfig Routing { get; set; } = new();
    /// <summary>
    /// Gets or sets the engagement.
    /// </summary>
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

/// <summary>
/// Represents the available authentication modes.
/// </summary>
public enum AuthMode
{
    /// <summary>
    /// Uses local administrator passcode authentication.
    /// </summary>
    LocalPasscode,

    /// <summary>
    /// Uses OpenID Connect authentication.
    /// </summary>
    Oidc,

    /// <summary>
    /// Disables authentication for development scenarios.
    /// </summary>
    Disabled
}

/// <summary>
/// Represents the auth config.
/// </summary>
public sealed class AuthConfig
{
    /// <summary>
    /// Gets or sets the mode.
    /// </summary>
    public AuthMode Mode { get; set; } = AuthMode.LocalPasscode;
    /// <summary>
    /// Gets or sets the session duration minutes.
    /// </summary>
    public int SessionDurationMinutes { get; set; } = 480;
    /// <summary>
    /// Gets or sets the token default expiration days.
    /// </summary>
    public int TokenDefaultExpirationDays { get; set; } = 90;
    /// <summary>
    /// Gets or sets the allowed origins.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
    /// <summary>
    /// Gets or sets the local.
    /// </summary>
    public LocalPasscodeConfig Local { get; set; } = new();
    /// <summary>
    /// Gets or sets the oidc.
    /// </summary>
    public OidcConfig Oidc { get; set; } = new();
    /// <summary>
    /// Gets or sets the rate limit.
    /// </summary>
    public RateLimitConfig RateLimit { get; set; } = new();
}

/// <summary>
/// Represents the local passcode config.
/// </summary>
public sealed class LocalPasscodeConfig
{
    /// <summary>
    /// Gets or sets the min length.
    /// </summary>
    public int MinLength { get; set; } = 8;
    /// <summary>
    /// Gets or sets the max failed attempts.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;
    /// <summary>
    /// Gets or sets the lockout minutes.
    /// </summary>
    public int LockoutMinutes { get; set; } = 15;
}

/// <summary>
/// Represents the oidc config.
/// </summary>
public sealed class OidcConfig
{
    /// <summary>
    /// Gets or sets the authority.
    /// </summary>
    public string Authority { get; set; } = "";
    /// <summary>
    /// Gets or sets the client id.
    /// </summary>
    public string ClientId { get; set; } = "";
    /// <summary>
    /// Gets or sets the client secret.
    /// </summary>
    public string ClientSecret { get; set; } = "";
    /// <summary>
    /// Gets or sets the callback path.
    /// </summary>
    public string CallbackPath { get; set; } = "/auth/oidc/callback";
    /// <summary>
    /// Gets or sets the scopes.
    /// </summary>
    public string[] Scopes { get; set; } = ["openid", "profile", "email"];
    /// <summary>
    /// Gets or sets the admin subject claim.
    /// </summary>
    public string AdminSubjectClaim { get; set; } = "";
    /// <summary>
    /// Gets or sets the admin claim type.
    /// </summary>
    public string AdminClaimType { get; set; } = "sub";
}

/// <summary>
/// Represents the rate limit config.
/// </summary>
public sealed class RateLimitConfig
{
    /// <summary>
    /// Gets or sets the login per minute per ip.
    /// </summary>
    public int LoginPerMinutePerIp { get; set; } = 5;
    /// <summary>
    /// Gets or sets the login per hour per ip.
    /// </summary>
    public int LoginPerHourPerIp { get; set; } = 20;
    /// <summary>
    /// Gets or sets the login per minute global.
    /// </summary>
    public int LoginPerMinuteGlobal { get; set; } = 50;
    /// <summary>
    /// Gets or sets the token creation per hour.
    /// </summary>
    public int TokenCreationPerHour { get; set; } = 10;
}

/// <summary>
/// Represents the lite llm config.
/// </summary>
public sealed class LiteLlmConfig
{
    /// <summary>
    /// Gets or sets the base url.
    /// </summary>
    public string BaseUrl { get; set; } = "http://litellm:4000";
    /// <summary>
    /// Gets or sets the api key.
    /// </summary>
    public string ApiKey { get; set; } = "sk-LeanKernel-local";
    /// <summary>
    /// Gets or sets the default model.
    /// </summary>
    public string DefaultModel { get; set; } = "small";
    /// <summary>
    /// Gets or sets the embedding model.
    /// </summary>
    public string EmbeddingModel { get; set; } = "embedding-small";
    /// <summary>
    /// Gets or sets the context window tokens.
    /// </summary>
    public int ContextWindowTokens { get; set; } = 128_000;
}

/// <summary>
/// Represents the ollama config.
/// </summary>
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

/// <summary>
/// Represents the qdrant config.
/// </summary>
public sealed class QdrantConfig
{
    /// <summary>
    /// Gets or sets the host.
    /// </summary>
    public string Host { get; set; } = "qdrant";
    /// <summary>
    /// Gets or sets the port.
    /// </summary>
    public int Port { get; set; } = 6334;
    /// <summary>
    /// Legacy collection name. Use Knowledge.CollectionName for the unified knowledge collection.
    /// Retained for backward compatibility with existing Qdrant data.
    /// </summary>
    public string CollectionName { get; set; } = "LEANKERNEL_knowledge";
    /// <summary>
    /// Gets or sets the embedding dimension.
    /// </summary>
    public int EmbeddingDimension { get; set; } = 1536;
}

/// <summary>
/// Represents the signal config.
/// </summary>
public sealed class SignalConfig
{
    /// <summary>
    /// Gets or sets the cli path.
    /// </summary>
    public string CliPath { get; set; } = "/usr/bin/signal-cli";
    /// <summary>
    /// Gets or sets the account.
    /// </summary>
    public string Account { get; set; } = "";
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Gets or sets the allowed senders.
    /// </summary>
    public string[] AllowedSenders { get; set; } = [];

    /// <summary>
    /// Base URL of the signal-daemon HTTP sidecar (e.g. "http://LeanKernel-signal:8080").
    /// When set, <see cref="SignalRestApiAdapter"/> is used instead of the local
    /// signal-cli child process, which eliminates config-lock contention.
    /// </summary>
    public string DaemonBaseUrl { get; set; } = "";
}

/// <summary>
/// Represents the unstructured config.
/// </summary>
public sealed class UnstructuredConfig
{
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Gets or sets the base url.
    /// </summary>
    public string BaseUrl { get; set; } = "http://unstructured:8000";
    /// <summary>
    /// Gets or sets the timeout seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
    /// <summary>
    /// Gets or sets the supported mime types.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the supported extensions.
    /// </summary>
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

/// <summary>
/// Represents the agents config.
/// </summary>
public sealed class AgentsConfig
{
    /// <summary>
    /// Gets or sets the base path.
    /// </summary>
    public string BasePath { get; set; } = "/app/data/agents";
}

/// <summary>
/// Represents the wiki config.
/// </summary>
public sealed class WikiConfig
{
    /// <summary>
    /// Gets or sets the base path.
    /// </summary>
    public string BasePath { get; set; } = "/app/data/wiki";

    /// <summary>
    /// Gets or sets the name of the metadata folder (default: ".meta").
    /// </summary>
    public string MetaFolder { get; set; } = ".meta";

    /// <summary>
    /// Gets or sets the max facts per entry.
    /// </summary>
    public int MaxFactsPerEntry { get; set; } = 20;

    /// <summary>
    /// Gets or sets the stale fact days.
    /// </summary>
    public int StaleFactDays { get; set; } = 30;

    /// <summary>
    /// Gets or sets the min confidence threshold.
    /// </summary>
    public double MinConfidenceThreshold { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets OpenClaw import settings for one-shot wiki ingestion.
    /// </summary>
    public OpenClawImportConfig OpenClawImport { get; set; } = new();

    /// <summary>
    /// Gets or sets the default extraction strategy (Deterministic or LLM).
    /// </summary>
    public WikiExtractionStrategy ExtractionStrategy { get; set; } = WikiExtractionStrategy.Deterministic;
}

/// <summary>
/// Configuration for importing wiki data from a remote OpenClaw host.
/// </summary>
public sealed class OpenClawImportConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether remote OpenClaw import is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the SSH host used to fetch OpenClaw wiki/session data.
    /// </summary>
    public string RemoteHost { get; set; } = "walnut";

    /// <summary>
    /// Gets or sets the remote path to the OpenClaw wiki root.
    /// </summary>
    public string RemoteWikiPath { get; set; } = "/home/achingono/openclaw/workspaces/main/wiki";

    /// <summary>
    /// Gets or sets the remote path to OpenClaw agents config root.
    /// </summary>
    public string RemoteAgentsPath { get; set; } = "/home/achingono/openclaw/config/agents";

    /// <summary>
    /// Gets or sets the local staging subfolder under wiki meta folder.
    /// </summary>
    public string StagingFolder { get; set; } = "imports/openclaw";
}

/// <summary>
/// Represents the context config.
/// </summary>
public sealed class ContextConfig
{
    /// <summary>
    /// Gets or sets the semantic similarity weight.
    /// </summary>
    public double SemanticSimilarityWeight { get; set; } = 0.40;
    /// <summary>
    /// Gets or sets the recency decay weight.
    /// </summary>
    public double RecencyDecayWeight { get; set; } = 0.20;
    /// <summary>
    /// Gets or sets the dimension match weight.
    /// </summary>
    public double DimensionMatchWeight { get; set; } = 0.25;
    /// <summary>
    /// Gets or sets the interaction frequency weight.
    /// </summary>
    public double InteractionFrequencyWeight { get; set; } = 0.15;
    /// <summary>
    /// Gets or sets the min relevance threshold.
    /// </summary>
    public double MinRelevanceThreshold { get; set; } = 0.65;
    /// <summary>
    /// Gets or sets the max conversation turns.
    /// </summary>
    public int MaxConversationTurns { get; set; } = 15;

    /// <summary>
    /// Gets or sets the reranker configuration used between retrieval and context assembly.
    /// </summary>
    public RerankerConfig Reranker { get; set; } = new();
}

/// <summary>
/// Configuration for retrieval reranking.
/// </summary>
public sealed class RerankerConfig
{
    /// <summary>
    /// Gets or sets whether reranking is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of top candidates to rerank.
    /// </summary>
    public int TopN { get; set; } = 12;

    /// <summary>
    /// Gets or sets the number of reranked candidates to keep.
    /// </summary>
    public int TopK { get; set; } = 6;

    /// <summary>
    /// Gets or sets the reranker timeout in milliseconds.
    /// </summary>
    public int TimeoutMs { get; set; } = 1200;

    /// <summary>
    /// Gets or sets the minimum acceptance score after reranking.
    /// </summary>
    public double MinAcceptanceScore { get; set; } = 0.0;
}

/// <summary>
/// Represents the knowledge config.
/// </summary>
public sealed class KnowledgeConfig
{
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
    public string CollectionName { get; set; } = "LEANKERNEL_knowledge";
    /// <summary>
    /// Gets or sets the wiki collection name.
    /// </summary>
    public string WikiCollectionName { get; set; } = "LEANKERNEL_knowledge";
    /// <summary>
    /// Gets or sets the documents collection name.
    /// </summary>
    public string DocumentsCollectionName { get; set; } = "documents";
    /// <summary>
    /// Gets or sets the embedding dimension.
    /// </summary>
    public int EmbeddingDimension { get; set; } = 1536;
    /// <summary>
    /// Gets or sets the documents path.
    /// </summary>
    public string DocumentsPath { get; set; } = "/app/data/agents/main/documents";
    /// <summary>
    /// Gets or sets the default document tags.
    /// </summary>
    public string[] DefaultDocumentTags { get; set; } = ["general"];
    /// <summary>
    /// Gets or sets the agent scopes.
    /// </summary>
    public Dictionary<string, AgentScopeConfig> AgentScopes { get; set; } = new();
    /// <summary>
    /// Gets or sets the tag rules.
    /// </summary>
    public List<TagRule> TagRules { get; set; } = [];
}

/// <summary>
/// Represents the agent scope config.
/// </summary>
public sealed class AgentScopeConfig
{
    /// <summary>
    /// Gets or sets the tags.
    /// </summary>
    public string[] Tags { get; set; } = [];
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = "";
}

/// <summary>
/// Represents the tag rule.
/// </summary>
public sealed class TagRule
{
    /// <summary>
    /// Gets or sets the path pattern.
    /// </summary>
    public string PathPattern { get; set; } = "";
    /// <summary>
    /// Gets or sets the tags.
    /// </summary>
    public string[] Tags { get; set; } = [];
}

/// <summary>
/// Represents the scheduler config.
/// </summary>
public sealed class SchedulerConfig
{
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Gets or sets the wiki maintenance cron.
    /// </summary>
    public string WikiMaintenanceCron { get; set; } = "0 3 * * *"; // 3 AM daily
    /// <summary>
    /// Gets or sets the chat fact scrub cron.
    /// </summary>
    public string ChatFactScrubCron { get; set; } = "30 2 * * *"; // 2:30 AM daily
    /// <summary>
    /// Gets or sets the user profile sync cron.
    /// </summary>
    public string UserProfileSyncCron { get; set; } = "0 4 * * *"; // 4 AM daily
}

/// <summary>
/// Represents the routing config.
/// </summary>
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

/// <summary>
/// Represents the spend guard config.
/// </summary>
public sealed class SpendGuardConfig
{
    /// <summary>Daily paid-request soft threshold (warning alert). 0 = disabled.</summary>
    public int DailyPaidRequestSoftLimit { get; set; } = 0;

    /// <summary>Daily paid-request hard threshold (disables paid fallback). 0 = disabled.</summary>
    public int DailyPaidRequestHardLimit { get; set; } = 0;
}
