using System.ClientModel;

using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Events;
using LeanKernel.Logic.Memory;
using LeanKernel.Logic.Providers;
using LeanKernel.Logic.Telemetry;
using LeanKernel.Logic.Tools;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using OpenAI;

namespace Microsoft.Extensions.DependencyInjection;

using LeanKernel.Logic.Filters;
using LeanKernel.Logic.Interfaces;
using LeanKernel.Logic.Policy;
using LeanKernel.Logic.Repositories;

/// <summary>
/// Provides extension methods for registering LeanKernel logic services.
/// </summary>
public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the scope filter infrastructure: policy provider, filter builder, and
    /// the open-generic <see cref="IFilter{TEntity}"/> service backed by <see cref="ScopeDrivenFilter{TEntity}"/>.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddFilters(this IServiceCollection services)
    {
        services.PostConfigure<EntityScopePolicies>(options =>
        {
            EnsureDefaultPolicy<SessionEntity>(options, ScopeDimension.Tenant | ScopeDimension.User | ScopeDimension.Channel);
            EnsureDefaultPolicy<TurnEntity>(options, ScopeDimension.Tenant | ScopeDimension.User | ScopeDimension.Channel, "Session");
            EnsureDefaultPolicy<TurnTelemetryEntity>(options, ScopeDimension.Tenant | ScopeDimension.User | ScopeDimension.Channel, "Turn.Session");
            EnsureDefaultPolicy<ChannelSenderBindingEntity>(options, ScopeDimension.Tenant | ScopeDimension.User | ScopeDimension.Channel);
            EnsureDefaultPolicy<ChannelMemoryPolicyEntity>(options, ScopeDimension.Tenant | ScopeDimension.Channel);
            EnsureDefaultPolicy<UserEntity>(options, ScopeDimension.Tenant | ScopeDimension.User);
            EnsureDefaultPolicy<TenantEntity>(options, ScopeDimension.Tenant);
        });

        services.AddSingleton<IScopePolicyProvider, ConfigurationScopePolicyProvider>();
        services.AddSingleton<ScopeFilterBuilder>();
        services.AddScoped(typeof(IFilter<>), typeof(ScopeDrivenFilter<>));
        return services;
    }

    private static void EnsureDefaultPolicy<TEntity>(
        EntityScopePolicies options,
        ScopeDimension dimensions,
        string? navigationPath = null,
        bool requireAuthentication = false)
        where TEntity : class
    {
        options.Policies ??= [];

        var entityType = typeof(TEntity).FullName!;
        var existing = options.Policies.FirstOrDefault(p =>
            string.Equals(p.EntityType, entityType, StringComparison.Ordinal)
            || string.Equals(p.EntityType, typeof(TEntity).Name, StringComparison.Ordinal));

        if (existing is not null)
        {
            return;
        }

        options.Policies.Add(new EntityScopePolicy
        {
            EntityType = entityType,
            Dimensions = dimensions,
            NavigationPath = navigationPath,
            RequireAuthentication = requireAuthentication,
        });
    }

    /// <summary>
    /// Registers the open-generic <see cref="IRepository{TEntity}"/> service backed by <see cref="EntityRepository{TEntity}"/>.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped(typeof(IRepository<>), typeof(EntityRepository<>));
        return services;
    }

    /// <summary>
    /// Registers chat history and memory providers scoped by identity, against base types.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddContextProviders(this IServiceCollection services)
    {
        services.AddScoped<ChatHistoryProvider, DbChatHistoryProvider>();
        services.AddScoped<AIContextProvider, MemoryProvider>();
        services.AddScoped<IChannelMemoryPolicyResolver, ChannelMemoryPolicyResolver>();
        services.AddScoped<IdentityContextAssembler>();
        services.AddMemoryPageServices();
        return services;
    }

    /// <summary>
    /// Registers telemetry capture, collector, cost estimation table, and configuration.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TelemetrySettings>(configuration.GetSection("Agents:Telemetry"));
        services.Configure<CostEstimateTable>(configuration.GetSection("Agents:Telemetry:CostEstimate"));
        services.AddScoped<ITurnTelemetryCollector, TurnTelemetryCollector>();
        services.AddScoped<ITelemetryAggregationService, TelemetryAggregationService>();
        services.AddScoped<ITelemetryExportService, TelemetryExportService>();
        return services;
    }

    /// <summary>
    /// Registers the event spine infrastructure: request-scoped collector and optional store.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddEventSpine(this IServiceCollection services)
    {
        services.AddScoped<IEventCollector, EventCollector>();
        services.AddScoped<IEventStore, DbEventStore>();
        return services;
    }

    /// <summary>
    /// Registers the shared policy core: policy context, evaluator, and default policies.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPolicyCore(this IServiceCollection services)
    {
        services.AddScoped<IPolicyContext>(sp =>
        {
            var permit = sp.GetRequiredService<IPermit>();
            return new PolicyContext(permit);
        });

        services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();

        // Register default domain policies
        services.AddScoped<IPolicy<object>, BudgetCheckPolicy>();
        services.AddScoped<IPolicy<UserEntity>, IdentityLinkingPolicy>();
        services.AddScoped<IPolicy<ChannelMemoryPolicyEntity>, MemoryAccessPolicy>();

        return services;
    }

    /// <summary>
    /// Registers the memory parsing, normalization, reasoning, and rendering services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddMemoryPageServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MemoryPageParser>();
        services.AddScoped<MemoryPageRenderer>();
        services.AddScoped<MemoryPageNormalizer>();
        services.AddScoped<MemoryDimensionClassifier>();
        services.AddScoped<MemoryPageLinker>();
        services.AddScoped<MemoryGraphReasoner>();
        services.AddScoped<MemoryFieldRepairService>();
        services.AddScoped<MemoryPageKeyBuilder>();
        services.AddScoped<FactExtractionService>();

        services.AddScoped<IReasoningModel>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<MemorySettings>>().Value;
            var logger = sp.GetRequiredService<ILogger<ReasoningModel>>();
            var chatClient = sp.GetRequiredKeyedService<IChatClient>("small-model");
            return new ReasoningModel(chatClient, cfg, logger);
        });

        return services;
    }

    /// <summary>
    /// Registers the OpenAI-compatible chat client from configuration.
    /// Uses the ToolModel alias for the primary chat client when configured.
    /// Optionally wraps with telemetry capture when enabled.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLeanKernelChatClient(this IServiceCollection services)
    {
        services.AddChatClient(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<OpenAISettings>>().Value;
            var agentSettings = sp.GetRequiredService<IOptions<AgentSettings>>().Value;

            // Use ToolModel alias for tool-capable turns when configured and tools are enabled
            var modelId = agentSettings.Tools.Enabled && !string.IsNullOrWhiteSpace(cfg.ToolModel)
                ? cfg.ToolModel
                : cfg.DefaultModel;

            var client = new OpenAIClient(
                new ApiKeyCredential(cfg.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(cfg.BaseUrl) });
            IChatClient chatClient = client.GetChatClient(modelId).AsIChatClient();

            // Wrap with telemetry capture when enabled
            var telemetrySettings = sp.GetRequiredService<IOptions<TelemetrySettings>>().Value;
            if (telemetrySettings.Enabled)
            {
                var collector = sp.GetRequiredService<ITurnTelemetryCollector>();
                var costTable = sp.GetRequiredService<IOptions<CostEstimateTable>>().Value;
                var logger = sp.GetRequiredService<ILogger<TelemetryCapturingChatClient>>();
                chatClient = new TelemetryCapturingChatClient(chatClient, collector, costTable,
                    sp.GetRequiredService<IOptions<TelemetrySettings>>(), logger);
            }

            return chatClient;
        })
        .UseFunctionInvocation()
        .UseLogging();

        services.AddKeyedScoped<IChatClient>("small-model", (sp, _) =>
        {
            var cfg = sp.GetRequiredService<IOptions<MemorySettings>>().Value;
            if (!cfg.Enabled)
            {
                return new DisabledChatClient();
            }

            var openAi = sp.GetRequiredService<IOptions<OpenAISettings>>().Value;
            var client = new OpenAIClient(
                new ApiKeyCredential(openAi.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(openAi.BaseUrl) });
            return client.GetChatClient(cfg.ModelId).AsIChatClient();
        });

        services.AddKeyedScoped<IChatClient>("fact-extraction", (sp, _) =>
        {
            var cfg = sp.GetRequiredService<IOptions<FactExtractionSettings>>().Value;
            var openAi = sp.GetRequiredService<IOptions<OpenAISettings>>().Value;
            var client = new OpenAIClient(
                new ApiKeyCredential(openAi.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(openAi.BaseUrl) });
            return client.GetChatClient(cfg.ModelId).AsIChatClient();
        });

        return services;
    }

    /// <summary>
    /// Registers a named <see cref="AIAgent"/> as scoped using MAF hosting primitives.
    /// Scoped lifetime ensures providers (ChatHistoryProvider, AIContextProvider) are resolved
    /// from the request scope rather than captured at singleton creation time.
    /// When the tool runtime is enabled, registered tools are attached through ChatOptions.Tools.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="agentName">The agent name to register.</param>
    /// <param name="configuration">The configuration used to bind agent settings.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLeanKernelAgent(
        this IServiceCollection services,
        string agentName,
        IConfiguration configuration)
    {
        services.Configure<AgentSettings>(configuration.GetSection("Agents"));

        services.AddAIAgent(agentName, (sp, name) =>
        {
            var settings = sp.GetRequiredService<IOptions<AgentSettings>>().Value;

            // Resolve tools from the registry when the tool runtime is enabled
            List<AITool> aiTools = [];
            if (settings.Tools.Enabled)
            {
                var registry = sp.GetService<IToolRegistry>();
                if (registry is not null)
                {
                    var policy = new ToolGovernancePolicy(settings.Tools);
                    aiTools = policy.Filter(registry.Tools)
                        .Select(ToolDefinitionAIToolAdapter.ToAITool)
                        .ToList();
                }
            }

            return new ChatClientAgent(
                sp.GetRequiredService<IChatClient>(),
                new ChatClientAgentOptions
                {
                    Name = name,
                    Description = settings.DefaultDescription,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = settings.DefaultInstructions,
                        Tools = aiTools.Count > 0 ? aiTools : null,
                    },
                    ChatHistoryProvider = sp.GetRequiredService<ChatHistoryProvider>(),
                    AIContextProviders = sp.GetServices<AIContextProvider>().ToList(),
                },
                sp.GetRequiredService<ILoggerFactory>(),
                sp);
        }, ServiceLifetime.Scoped);

        return services;
    }
}
