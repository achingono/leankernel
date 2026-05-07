using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Models.Admin;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/config")]
[Authorize(Policy = AuthConstants.PolicyAdminOnly)]
public sealed class ConfigController : ControllerBase
{
    private readonly IOptions<LeanKernelConfig> _config;

    public ConfigController(IOptions<LeanKernelConfig> config)
    {
        _config = config;
    }

    /// <summary>
    /// Returns the full admin configuration contract covering all supported LeanKernelConfig sections.
    /// Secrets are masked or shown as reference indicators only.
    /// Each field carries provenance metadata: mutability, restart-required, env-backed, secret status.
    /// </summary>
    [HttpGet]
    public IActionResult GetConfig()
    {
        var cfg = _config.Value;
        return Ok(BuildResponse(cfg));
    }

    public static AdminConfigResponse BuildResponse(LeanKernelConfig cfg)
    {
        return new AdminConfigResponse
        {
            LiteLlm = new LiteLlmConfigSection
            {
                BaseUrl = Field(cfg.LiteLlm.BaseUrl, description: "LiteLLM proxy base URL"),
                ApiKey = SecretField(cfg.LiteLlm.ApiKey, description: "API key for LiteLLM proxy authentication"),
                DefaultModel = Field(cfg.LiteLlm.DefaultModel, description: "Default chat model alias"),
                EmbeddingModel = Field(cfg.LiteLlm.EmbeddingModel, description: "Default embedding model alias"),
                ContextWindowTokens = Field(cfg.LiteLlm.ContextWindowTokens, description: "Maximum context window size in tokens")
            },
            Qdrant = new QdrantConfigSection
            {
                Host = Field(cfg.Qdrant.Host, restartRequired: true, description: "Qdrant server hostname"),
                Port = Field(cfg.Qdrant.Port, restartRequired: true, description: "Qdrant gRPC port"),
                CollectionName = Field(cfg.Qdrant.CollectionName, restartRequired: true, description: "Legacy Qdrant collection name"),
                EmbeddingDimension = Field(cfg.Qdrant.EmbeddingDimension, restartRequired: true, description: "Vector embedding dimension")
            },
            Signal = new SignalConfigSection
            {
                CliPath = Field(cfg.Signal.CliPath, description: "Path to signal-cli binary"),
                Account = SecretField(cfg.Signal.Account, description: "Signal account phone number"),
                Enabled = Field(cfg.Signal.Enabled, description: "Enable Signal channel"),
                AllowedSenders = Field(cfg.Signal.AllowedSenders, description: "Phone numbers allowed to send messages"),
                DaemonBaseUrl = Field(cfg.Signal.DaemonBaseUrl, description: "Signal daemon HTTP sidecar URL (replaces local CLI when set)")
            },
            Unstructured = new UnstructuredConfigSection
            {
                Enabled = Field(cfg.Unstructured.Enabled, description: "Enable Unstructured document processing"),
                BaseUrl = Field(cfg.Unstructured.BaseUrl, description: "Unstructured service base URL"),
                TimeoutSeconds = Field(cfg.Unstructured.TimeoutSeconds, description: "Request timeout for document processing")
            },
            Wiki = new WikiConfigSection
            {
                BasePath = Field(cfg.Wiki.BasePath, description: "Filesystem path to wiki storage"),
                MaxFactsPerEntry = Field(cfg.Wiki.MaxFactsPerEntry, description: "Maximum facts retained per wiki entry"),
                StaleFactDays = Field(cfg.Wiki.StaleFactDays, description: "Days after which a fact is considered stale"),
                MinConfidenceThreshold = Field(cfg.Wiki.MinConfidenceThreshold, description: "Minimum confidence score to retain a fact")
            },
            Agents = new AgentsConfigSection
            {
                BasePath = Field(cfg.Agents.BasePath, description: "Filesystem path to agent configuration files")
            },
            Knowledge = new KnowledgeConfigSection
            {
                Enabled = Field(cfg.Knowledge.Enabled, description: "Enable the knowledge retrieval system"),
                CollectionName = Field(cfg.Knowledge.CollectionName, description: "Qdrant collection name for knowledge documents"),
                EmbeddingDimension = Field(cfg.Knowledge.EmbeddingDimension, description: "Embedding vector dimension for knowledge documents"),
                DocumentsPath = Field(cfg.Knowledge.DocumentsPath, description: "Filesystem path for knowledge documents"),
                DefaultDocumentTags = Field(cfg.Knowledge.DefaultDocumentTags, description: "Default tags applied to newly ingested documents")
            },
            Context = new ContextConfigSection
            {
                SemanticSimilarityWeight = Field(cfg.Context.SemanticSimilarityWeight, description: "Weight for semantic similarity in context scoring"),
                RecencyDecayWeight = Field(cfg.Context.RecencyDecayWeight, description: "Weight for recency decay in context scoring"),
                DimensionMatchWeight = Field(cfg.Context.DimensionMatchWeight, description: "Weight for dimension matching in context scoring"),
                InteractionFrequencyWeight = Field(cfg.Context.InteractionFrequencyWeight, description: "Weight for interaction frequency in context scoring"),
                MinRelevanceThreshold = Field(cfg.Context.MinRelevanceThreshold, description: "Minimum relevance score for context inclusion"),
                MaxConversationTurns = Field(cfg.Context.MaxConversationTurns, description: "Maximum conversation turns retained in context window")
            },
            Scheduler = new SchedulerConfigSection
            {
                Enabled = Field(cfg.Scheduler.Enabled, description: "Enable background scheduler"),
                WikiMaintenanceCron = Field(cfg.Scheduler.WikiMaintenanceCron, description: "Cron expression for wiki maintenance job")
            },
            Auth = new AuthConfigSection
            {
                Mode = Field(cfg.Auth.Mode.ToString(), restartRequired: true, description: "Authentication mode (LocalPasscode, Oidc, Disabled)"),
                SessionDurationMinutes = Field(cfg.Auth.SessionDurationMinutes, restartRequired: true, description: "Session duration in minutes"),
                TokenDefaultExpirationDays = Field(cfg.Auth.TokenDefaultExpirationDays, description: "Default API token expiration in days"),
                Local = new LocalPasscodeConfigSection
                {
                    MinLength = Field(cfg.Auth.Local.MinLength, description: "Minimum passcode length"),
                    MaxFailedAttempts = Field(cfg.Auth.Local.MaxFailedAttempts, description: "Failed attempts before lockout"),
                    LockoutMinutes = Field(cfg.Auth.Local.LockoutMinutes, description: "Lockout duration in minutes")
                },
                Oidc = new OidcConfigSection
                {
                    Authority = Field(cfg.Auth.Oidc.Authority, restartRequired: true, description: "OIDC provider authority URL"),
                    ClientId = Field(cfg.Auth.Oidc.ClientId, restartRequired: true, description: "OIDC client identifier"),
                    ClientSecret = SecretField(cfg.Auth.Oidc.ClientSecret, envBacked: true, restartRequired: true, description: "OIDC client secret (env-backed)"),
                    CallbackPath = Field(cfg.Auth.Oidc.CallbackPath, restartRequired: true, description: "OIDC callback path"),
                    AdminSubjectClaim = Field(cfg.Auth.Oidc.AdminSubjectClaim, description: "Subject claim value that grants admin access")
                },
                RateLimit = new RateLimitConfigSection
                {
                    LoginPerMinutePerIp = Field(cfg.Auth.RateLimit.LoginPerMinutePerIp, description: "Max login attempts per minute per IP"),
                    LoginPerHourPerIp = Field(cfg.Auth.RateLimit.LoginPerHourPerIp, description: "Max login attempts per hour per IP"),
                    LoginPerMinuteGlobal = Field(cfg.Auth.RateLimit.LoginPerMinuteGlobal, description: "Max login attempts per minute globally"),
                    TokenCreationPerHour = Field(cfg.Auth.RateLimit.TokenCreationPerHour, description: "Max API token creations per hour")
                }
            },
            Routing = new RoutingConfigSection
            {
                Enabled = Field(cfg.Routing.Enabled, description: "Enable intelligent model routing pipeline"),
                ShadowMode = Field(cfg.Routing.ShadowMode, description: "Shadow mode: routing runs in parallel but responses are discarded"),
                EnableQualityEscalation = Field(cfg.Routing.EnableQualityEscalation, description: "Enable quality-based escalation and retry logic"),
                SmallMaxTokens = Field(cfg.Routing.SmallMaxTokens, description: "Max token count for 'small' complexity tier"),
                MediumMaxTokens = Field(cfg.Routing.MediumMaxTokens, description: "Max token count for 'medium' complexity tier"),
                SmallAlias = Field(cfg.Routing.SmallAlias, description: "LiteLLM alias for the small tier"),
                MediumAlias = Field(cfg.Routing.MediumAlias, description: "LiteLLM alias for the medium tier"),
                LargeAlias = Field(cfg.Routing.LargeAlias, description: "LiteLLM alias for the large tier"),
                CooldownSeconds = Field(cfg.Routing.CooldownSeconds, description: "Provider cooldown duration after failure"),
                MaxProviderAttempts = Field(cfg.Routing.MaxProviderAttempts, description: "Maximum provider attempts per request"),
                SpendGuard = new SpendGuardConfigSection
                {
                    DailyPaidRequestSoftLimit = Field(cfg.Routing.SpendGuard.DailyPaidRequestSoftLimit, description: "Daily paid-request soft limit (warning; 0 = disabled)"),
                    DailyPaidRequestHardLimit = Field(cfg.Routing.SpendGuard.DailyPaidRequestHardLimit, description: "Daily paid-request hard limit (disables paid fallback; 0 = disabled)")
                }
            },
            Engagement = new EngagementConfigSection
            {
                SourceOfTruth = new ConfigField
                {
                    Value = "AGENTS.md + runtime JSON",
                    Mutable = false,
                    Description = "Engagement rules are governed by the agent config files. Use the Agents workspace to edit them."
                }
            },
            Channels = new ChannelsConfigSection
            {
                SignalPhoneNumber = SecretField(cfg.SignalPhoneNumber, envBacked: true, description: "Signal phone number for the REST API channel"),
                SignalServerUrl = Field(cfg.SignalServerUrl, description: "Signal REST API server URL"),
                SignalApiToken = SecretField(cfg.SignalApiToken, envBacked: true, description: "Signal REST API authentication token"),
                DiscordBotToken = SecretField(cfg.DiscordBotToken, envBacked: true, description: "Discord bot token"),
                DiscordChannelId = Field(cfg.DiscordChannelId, description: "Discord channel ID for the bot")
            }
        };
    }

    public static ConfigField Field(
        object? value,
        bool restartRequired = false,
        bool mutable = true,
        string? description = null) =>
        new()
        {
            Value = value,
            Masked = false,
            EnvBacked = false,
            RestartRequired = restartRequired,
            Mutable = mutable,
            Description = description
        };

    public static ConfigField SecretField(
        string? value,
        bool envBacked = false,
        bool restartRequired = false,
        string? description = null) =>
        new()
        {
            Value = MaskSecret(value),
            Masked = true,
            EnvBacked = envBacked,
            RestartRequired = restartRequired,
            Mutable = true,
            Description = description
        };

    private static string MaskSecret(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 8)
            return "***";
        return value[..4] + new string('*', value.Length - 4);
    }
}

