namespace LeanKernel.Core.Configuration;

/// <summary>
/// Root configuration model, bound from appsettings.json under "LeanKernel" key.
/// </summary>
public sealed class LeanKernelConfig
{
    public const string SectionName = "LeanKernel";

    public LiteLlmConfig LiteLlm { get; set; } = new();
    public QdrantConfig Qdrant { get; set; } = new();
    public SignalConfig Signal { get; set; } = new();
    public WikiConfig Wiki { get; set; } = new();
    public KnowledgeConfig Knowledge { get; set; } = new();
    public ContextConfig Context { get; set; } = new();
    public SchedulerConfig Scheduler { get; set; } = new();
    public AuthConfig Auth { get; set; } = new();
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
    public string DefaultModel { get; set; } = "gpt-4o-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int ContextWindowTokens { get; set; } = 128_000;
}

public sealed class QdrantConfig
{
    public string Host { get; set; } = "qdrant";
    public int Port { get; set; } = 6334;
    public string CollectionName { get; set; } = "LEANKERNEL_wiki";
    public int EmbeddingDimension { get; set; } = 1536;
}

public sealed class SignalConfig
{
    public string CliPath { get; set; } = "/usr/local/bin/signal-cli";
    public string Account { get; set; } = "";
    public bool Enabled { get; set; } = true;
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
    public string DocumentsPath { get; set; } = "/app/data/documents";
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
}
