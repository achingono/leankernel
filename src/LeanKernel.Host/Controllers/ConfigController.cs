using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Models.Admin;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

/// <summary>
/// Represents the config controller.
/// </summary>
[ApiController]
[Route("api/config")]
[Authorize(Policy = AuthConstants.PolicyAdminOnly)]
public sealed class ConfigController : ControllerBase
{
    private readonly IOptions<LeanKernelConfig> _config;
    private readonly IRuntimeLeanKernelConfigStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigController" /> class.
    /// </summary>
    /// <param name="config">The config.</param>
    /// <param name="store">The store.</param>
    public ConfigController(IOptions<LeanKernelConfig> config, IRuntimeLeanKernelConfigStore store)
    {
        _config = config;
        _store = store;
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

    /// <summary>
    /// Executes the build response operation.
    /// </summary>
    /// <param name="cfg">The cfg.</param>
    /// <returns>The operation result.</returns>
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
                TimeoutSeconds = Field(cfg.Unstructured.TimeoutSeconds, description: "Request timeout for document processing"),
                SupportedMimeTypes = Field(cfg.Unstructured.SupportedMimeTypes, description: "Attachment MIME types routed through the extractor"),
                SupportedExtensions = Field(cfg.Unstructured.SupportedExtensions, description: "Attachment file extensions routed through the extractor")
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
                WikiMaintenanceCron = Field(cfg.Scheduler.WikiMaintenanceCron, description: "Cron expression for wiki maintenance job"),
                ChatFactScrubCron = Field(cfg.Scheduler.ChatFactScrubCron, description: "Cron expression for nightly chat fact scrub job")
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

    /// <summary>
    /// Represents the field.
    /// </summary>
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

    /// <summary>
    /// Represents the secret field.
    /// </summary>
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

    /// <summary>
    /// Applies a partial config update to the runtime settings file.
    /// Only non-null patch values are applied; secrets cannot be changed here.
    /// Returns a diff of applied changes and the updated full config.
    /// </summary>
    [HttpPatch]
    public async Task<IActionResult> PatchConfig(
        [FromBody] AdminConfigPatchRequest patch,
        CancellationToken ct)
    {
        var current = _store.GetCurrent();
        var (updated, changes) = ApplyPatch(current, patch);

        if (changes.Count == 0)
            return Ok(new AdminConfigPatchResponse { Changes = [], UpdatedConfig = BuildResponse(updated) });

        await _store.SaveAsync(updated, ct);
        return Ok(new AdminConfigPatchResponse { Changes = changes, UpdatedConfig = BuildResponse(updated) });
    }

    /// <summary>
    /// Applies a patch request to a copy of the current configuration.
    /// </summary>
    /// <param name="current">The configuration values to patch.</param>
    /// <param name="patch">The requested configuration updates.</param>
    /// <returns>The operation result.</returns>
    public static (LeanKernelConfig updated, List<ConfigChange> changes) ApplyPatch(
        LeanKernelConfig current,
        AdminConfigPatchRequest patch)
    {
        var changes = new List<ConfigChange>();

        // Clone the current config to apply changes
        var liteLlm = current.LiteLlm;
        var qdrant = current.Qdrant;
        var signal = current.Signal;
        var unstructured = current.Unstructured;
        var wiki = current.Wiki;
        var agents = current.Agents;
        var knowledge = current.Knowledge;
        var context = current.Context;
        var scheduler = current.Scheduler;
        var routing = current.Routing;

        if (patch.LiteLlm is { } lp)
        {
            liteLlm = new LiteLlmConfig
            {
                BaseUrl = ApplyString(liteLlm.BaseUrl, lp.BaseUrl, "LiteLlm", "BaseUrl", changes),
                ApiKey = liteLlm.ApiKey, // never overwritten via PATCH
                DefaultModel = ApplyString(liteLlm.DefaultModel, lp.DefaultModel, "LiteLlm", "DefaultModel", changes),
                EmbeddingModel = ApplyString(liteLlm.EmbeddingModel, lp.EmbeddingModel, "LiteLlm", "EmbeddingModel", changes),
                ContextWindowTokens = ApplyInt(liteLlm.ContextWindowTokens, lp.ContextWindowTokens, "LiteLlm", "ContextWindowTokens", changes)
            };
        }

        if (patch.Qdrant is { } qp)
        {
            qdrant = new QdrantConfig
            {
                Host = ApplyString(qdrant.Host, qp.Host, "Qdrant", "Host", changes, restartRequired: true),
                Port = ApplyInt(qdrant.Port, qp.Port, "Qdrant", "Port", changes, restartRequired: true),
                CollectionName = ApplyString(qdrant.CollectionName, qp.CollectionName, "Qdrant", "CollectionName", changes, restartRequired: true),
                EmbeddingDimension = ApplyInt(qdrant.EmbeddingDimension, qp.EmbeddingDimension, "Qdrant", "EmbeddingDimension", changes, restartRequired: true)
            };
        }

        if (patch.Signal is { } sp)
        {
            signal = new SignalConfig
            {
                CliPath = ApplyString(signal.CliPath, sp.CliPath, "Signal", "CliPath", changes),
                Account = signal.Account, // secret — not patchable
                Enabled = ApplyBool(signal.Enabled, sp.Enabled, "Signal", "Enabled", changes),
                AllowedSenders = sp.AllowedSenders ?? signal.AllowedSenders,
                DaemonBaseUrl = ApplyString(signal.DaemonBaseUrl, sp.DaemonBaseUrl, "Signal", "DaemonBaseUrl", changes)
            };
            if (sp.AllowedSenders != null && !sp.AllowedSenders.SequenceEqual(current.Signal.AllowedSenders))
            {
                changes.Add(new ConfigChange
                {
                    Section = "Signal",
                    Field = "AllowedSenders",
                    OldValue = string.Join(", ", current.Signal.AllowedSenders),
                    NewValue = string.Join(", ", sp.AllowedSenders)
                });
            }
        }

        if (patch.Unstructured is { } up)
        {
            unstructured = new UnstructuredConfig
            {
                Enabled = ApplyBool(unstructured.Enabled, up.Enabled, "Unstructured", "Enabled", changes),
                BaseUrl = ApplyString(unstructured.BaseUrl, up.BaseUrl, "Unstructured", "BaseUrl", changes),
                TimeoutSeconds = ApplyInt(unstructured.TimeoutSeconds, up.TimeoutSeconds, "Unstructured", "TimeoutSeconds", changes),
                SupportedMimeTypes = up.SupportedMimeTypes ?? unstructured.SupportedMimeTypes,
                SupportedExtensions = up.SupportedExtensions ?? unstructured.SupportedExtensions
            };

            if (up.SupportedMimeTypes != null && !up.SupportedMimeTypes.SequenceEqual(current.Unstructured.SupportedMimeTypes))
            {
                changes.Add(new ConfigChange
                {
                    Section = "Unstructured",
                    Field = "SupportedMimeTypes",
                    OldValue = string.Join(", ", current.Unstructured.SupportedMimeTypes),
                    NewValue = string.Join(", ", up.SupportedMimeTypes)
                });
            }

            if (up.SupportedExtensions != null && !up.SupportedExtensions.SequenceEqual(current.Unstructured.SupportedExtensions))
            {
                changes.Add(new ConfigChange
                {
                    Section = "Unstructured",
                    Field = "SupportedExtensions",
                    OldValue = string.Join(", ", current.Unstructured.SupportedExtensions),
                    NewValue = string.Join(", ", up.SupportedExtensions)
                });
            }
        }

        if (patch.Wiki is { } wp)
        {
            wiki = new WikiConfig
            {
                BasePath = ApplyString(wiki.BasePath, wp.BasePath, "Wiki", "BasePath", changes),
                MaxFactsPerEntry = ApplyInt(wiki.MaxFactsPerEntry, wp.MaxFactsPerEntry, "Wiki", "MaxFactsPerEntry", changes),
                StaleFactDays = ApplyInt(wiki.StaleFactDays, wp.StaleFactDays, "Wiki", "StaleFactDays", changes),
                MinConfidenceThreshold = ApplyDouble(wiki.MinConfidenceThreshold, wp.MinConfidenceThreshold, "Wiki", "MinConfidenceThreshold", changes)
            };
        }

        if (patch.Agents is { } agp)
        {
            agents = new AgentsConfig
            {
                BasePath = ApplyString(agents.BasePath, agp.BasePath, "Agents", "BasePath", changes)
            };
        }

        if (patch.Knowledge is { } knp)
        {
            knowledge = new KnowledgeConfig
            {
                Enabled = ApplyBool(knowledge.Enabled, knp.Enabled, "Knowledge", "Enabled", changes),
                CollectionName = ApplyString(knowledge.CollectionName, knp.CollectionName, "Knowledge", "CollectionName", changes),
                EmbeddingDimension = knowledge.EmbeddingDimension,
                DocumentsPath = ApplyString(knowledge.DocumentsPath, knp.DocumentsPath, "Knowledge", "DocumentsPath", changes),
                DefaultDocumentTags = knp.DefaultDocumentTags ?? knowledge.DefaultDocumentTags,
                AgentScopes = knowledge.AgentScopes,
                TagRules = knowledge.TagRules
            };
            if (knp.DefaultDocumentTags != null && !knp.DefaultDocumentTags.SequenceEqual(current.Knowledge.DefaultDocumentTags))
            {
                changes.Add(new ConfigChange
                {
                    Section = "Knowledge",
                    Field = "DefaultDocumentTags",
                    OldValue = string.Join(", ", current.Knowledge.DefaultDocumentTags),
                    NewValue = string.Join(", ", knp.DefaultDocumentTags)
                });
            }
        }

        if (patch.Context is { } cp)
        {
            context = new ContextConfig
            {
                SemanticSimilarityWeight = ApplyDouble(context.SemanticSimilarityWeight, cp.SemanticSimilarityWeight, "Context", "SemanticSimilarityWeight", changes),
                RecencyDecayWeight = ApplyDouble(context.RecencyDecayWeight, cp.RecencyDecayWeight, "Context", "RecencyDecayWeight", changes),
                DimensionMatchWeight = ApplyDouble(context.DimensionMatchWeight, cp.DimensionMatchWeight, "Context", "DimensionMatchWeight", changes),
                InteractionFrequencyWeight = ApplyDouble(context.InteractionFrequencyWeight, cp.InteractionFrequencyWeight, "Context", "InteractionFrequencyWeight", changes),
                MinRelevanceThreshold = ApplyDouble(context.MinRelevanceThreshold, cp.MinRelevanceThreshold, "Context", "MinRelevanceThreshold", changes),
                MaxConversationTurns = ApplyInt(context.MaxConversationTurns, cp.MaxConversationTurns, "Context", "MaxConversationTurns", changes)
            };
        }

        if (patch.Scheduler is { } scp)
        {
            scheduler = new SchedulerConfig
            {
                Enabled = ApplyBool(scheduler.Enabled, scp.Enabled, "Scheduler", "Enabled", changes),
                WikiMaintenanceCron = ApplyString(scheduler.WikiMaintenanceCron, scp.WikiMaintenanceCron, "Scheduler", "WikiMaintenanceCron", changes),
                ChatFactScrubCron = ApplyString(scheduler.ChatFactScrubCron, scp.ChatFactScrubCron, "Scheduler", "ChatFactScrubCron", changes)
            };
        }

        if (patch.Routing is { } rp)
        {
            routing = new RoutingConfig
            {
                Enabled = ApplyBool(routing.Enabled, rp.Enabled, "Routing", "Enabled", changes),
                ShadowMode = ApplyBool(routing.ShadowMode, rp.ShadowMode, "Routing", "ShadowMode", changes),
                EnableQualityEscalation = ApplyBool(routing.EnableQualityEscalation, rp.EnableQualityEscalation, "Routing", "EnableQualityEscalation", changes),
                SmallMaxTokens = ApplyInt(routing.SmallMaxTokens, rp.SmallMaxTokens, "Routing", "SmallMaxTokens", changes),
                MediumMaxTokens = ApplyInt(routing.MediumMaxTokens, rp.MediumMaxTokens, "Routing", "MediumMaxTokens", changes),
                SmallAlias = ApplyString(routing.SmallAlias, rp.SmallAlias, "Routing", "SmallAlias", changes),
                MediumAlias = ApplyString(routing.MediumAlias, rp.MediumAlias, "Routing", "MediumAlias", changes),
                LargeAlias = ApplyString(routing.LargeAlias, rp.LargeAlias, "Routing", "LargeAlias", changes),
                CooldownSeconds = ApplyInt(routing.CooldownSeconds, rp.CooldownSeconds, "Routing", "CooldownSeconds", changes),
                MaxProviderAttempts = ApplyInt(routing.MaxProviderAttempts, rp.MaxProviderAttempts, "Routing", "MaxProviderAttempts", changes),
                SmallMaxConstraints = routing.SmallMaxConstraints,
                MediumMaxConstraints = routing.MediumMaxConstraints,
                MaxSelectionBudgetMs = routing.MaxSelectionBudgetMs,
                QualityMinOutputLength = routing.QualityMinOutputLength,
                QualityMinConstraintCoverage = routing.QualityMinConstraintCoverage,
                ModelLimitSyncCron = routing.ModelLimitSyncCron,
                SpendGuard = new SpendGuardConfig
                {
                    DailyPaidRequestSoftLimit = ApplyInt(routing.SpendGuard.DailyPaidRequestSoftLimit, rp.DailyPaidRequestSoftLimit, "Routing.SpendGuard", "DailyPaidRequestSoftLimit", changes),
                    DailyPaidRequestHardLimit = ApplyInt(routing.SpendGuard.DailyPaidRequestHardLimit, rp.DailyPaidRequestHardLimit, "Routing.SpendGuard", "DailyPaidRequestHardLimit", changes)
                }
            };
        }

        var updated = new LeanKernelConfig
        {
            LiteLlm = liteLlm,
            Qdrant = qdrant,
            Signal = signal,
            Unstructured = unstructured,
            Wiki = wiki,
            Agents = agents,
            Knowledge = knowledge,
            Context = context,
            Scheduler = scheduler,
            Auth = current.Auth,
            Routing = routing,
            Engagement = current.Engagement,
            SignalPhoneNumber = current.SignalPhoneNumber,
            SignalServerUrl = current.SignalServerUrl,
            SignalApiToken = current.SignalApiToken,
            DiscordBotToken = current.DiscordBotToken,
            DiscordChannelId = current.DiscordChannelId
        };

        return (updated, changes);
    }

    private static string ApplyString(string current, string? patch, string section, string field, List<ConfigChange> changes, bool restartRequired = false)
    {
        if (patch == null || patch == current)
            return current;
        changes.Add(new ConfigChange { Section = section, Field = field, OldValue = current, NewValue = patch, RestartRequired = restartRequired });
        return patch;
    }

    private static int ApplyInt(int current, int? patch, string section, string field, List<ConfigChange> changes, bool restartRequired = false)
    {
        if (patch == null || patch == current)
            return current;
        changes.Add(new ConfigChange { Section = section, Field = field, OldValue = current.ToString(), NewValue = patch.ToString(), RestartRequired = restartRequired });
        return patch.Value;
    }

    private static double ApplyDouble(double current, double? patch, string section, string field, List<ConfigChange> changes)
    {
        if (patch == null || Math.Abs(patch.Value - current) < 1e-10)
            return current;
        changes.Add(new ConfigChange { Section = section, Field = field, OldValue = current.ToString("G"), NewValue = patch.Value.ToString("G") });
        return patch.Value;
    }

    private static bool ApplyBool(bool current, bool? patch, string section, string field, List<ConfigChange> changes)
    {
        if (patch == null || patch == current)
            return current;
        changes.Add(new ConfigChange { Section = section, Field = field, OldValue = current.ToString(), NewValue = patch.ToString() });
        return patch.Value;
    }

    private static string MaskSecret(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 8)
            return "***";
        return value[..4] + new string('*', value.Length - 4);
    }
}
