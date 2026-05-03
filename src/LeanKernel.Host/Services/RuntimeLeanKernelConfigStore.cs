using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Services;

public sealed class RuntimeLeanKernelConfigStore : IRuntimeLeanKernelConfigStore
{
    private readonly IOptionsMonitor<LeanKernelConfig> _config;
    private readonly LeanKernelHostPaths _paths;
    private readonly ILogger<RuntimeLeanKernelConfigStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public RuntimeLeanKernelConfigStore(
        IOptionsMonitor<LeanKernelConfig> config,
        LeanKernelHostPaths paths,
        ILogger<RuntimeLeanKernelConfigStore> logger)
    {
        _config = config;
        _paths = paths;
        _logger = logger;
        Directory.CreateDirectory(_paths.DataDirectory);
    }

    public LeanKernelConfig GetCurrent() => Clone(_config.CurrentValue);

    public async Task SaveAsync(LeanKernelConfig config, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.DataDirectory);

        var payload = new RuntimeConfigDocument
        {
            LeanKernel = Clone(config)
        };

        var tempPath = _paths.RuntimeConfigPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, ct);
        }

        File.Move(tempPath, _paths.RuntimeConfigPath, overwrite: true);
        _logger.LogInformation("Persisted runtime configuration to {Path}", _paths.RuntimeConfigPath);
    }

    private static LeanKernelConfig Clone(LeanKernelConfig source) => new()
    {
        LiteLlm = new LiteLlmConfig
        {
            BaseUrl = source.LiteLlm.BaseUrl,
            ApiKey = source.LiteLlm.ApiKey,
            DefaultModel = source.LiteLlm.DefaultModel,
            EmbeddingModel = source.LiteLlm.EmbeddingModel,
            ContextWindowTokens = source.LiteLlm.ContextWindowTokens
        },
        Qdrant = new QdrantConfig
        {
            Host = source.Qdrant.Host,
            Port = source.Qdrant.Port,
            CollectionName = source.Qdrant.CollectionName,
            EmbeddingDimension = source.Qdrant.EmbeddingDimension
        },
        Signal = new SignalConfig
        {
            CliPath = source.Signal.CliPath,
            Account = source.Signal.Account,
            Enabled = source.Signal.Enabled,
            AllowedSenders = source.Signal.AllowedSenders?.ToArray() ?? []
        },
        Wiki = new WikiConfig
        {
            BasePath = source.Wiki.BasePath,
            MaxFactsPerEntry = source.Wiki.MaxFactsPerEntry,
            StaleFactDays = source.Wiki.StaleFactDays,
            MinConfidenceThreshold = source.Wiki.MinConfidenceThreshold
        },
        Context = new ContextConfig
        {
            SemanticSimilarityWeight = source.Context.SemanticSimilarityWeight,
            RecencyDecayWeight = source.Context.RecencyDecayWeight,
            DimensionMatchWeight = source.Context.DimensionMatchWeight,
            InteractionFrequencyWeight = source.Context.InteractionFrequencyWeight,
            MinRelevanceThreshold = source.Context.MinRelevanceThreshold,
            MaxConversationTurns = source.Context.MaxConversationTurns
        },
        Scheduler = new SchedulerConfig
        {
            Enabled = source.Scheduler.Enabled,
            WikiMaintenanceCron = source.Scheduler.WikiMaintenanceCron
        },
        Auth = new AuthConfig
        {
            Mode = source.Auth.Mode,
            SessionDurationMinutes = source.Auth.SessionDurationMinutes,
            TokenDefaultExpirationDays = source.Auth.TokenDefaultExpirationDays,
            AllowedOrigins = source.Auth.AllowedOrigins.ToArray(),
            Local = new LocalPasscodeConfig
            {
                MinLength = source.Auth.Local.MinLength,
                MaxFailedAttempts = source.Auth.Local.MaxFailedAttempts,
                LockoutMinutes = source.Auth.Local.LockoutMinutes
            },
            Oidc = new OidcConfig
            {
                Authority = source.Auth.Oidc.Authority,
                ClientId = source.Auth.Oidc.ClientId,
                ClientSecret = source.Auth.Oidc.ClientSecret,
                CallbackPath = source.Auth.Oidc.CallbackPath,
                Scopes = source.Auth.Oidc.Scopes.ToArray(),
                AdminSubjectClaim = source.Auth.Oidc.AdminSubjectClaim,
                AdminClaimType = source.Auth.Oidc.AdminClaimType
            },
            RateLimit = new RateLimitConfig
            {
                LoginPerMinutePerIp = source.Auth.RateLimit.LoginPerMinutePerIp,
                LoginPerHourPerIp = source.Auth.RateLimit.LoginPerHourPerIp,
                LoginPerMinuteGlobal = source.Auth.RateLimit.LoginPerMinuteGlobal,
                TokenCreationPerHour = source.Auth.RateLimit.TokenCreationPerHour
            }
        },
        Knowledge = new KnowledgeConfig
        {
            Enabled = source.Knowledge.Enabled,
            CollectionName = source.Knowledge.CollectionName,
            EmbeddingDimension = source.Knowledge.EmbeddingDimension,
            DocumentsPath = source.Knowledge.DocumentsPath,
            DefaultDocumentTags = source.Knowledge.DefaultDocumentTags.ToArray(),
            AgentScopes = new Dictionary<string, AgentScopeConfig>(source.Knowledge.AgentScopes),
            TagRules = source.Knowledge.TagRules.ToList()
        }
    };

    private sealed class RuntimeConfigDocument
    {
        public LeanKernelConfig LeanKernel { get; init; } = new();
    }
}
