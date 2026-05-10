using Microsoft.Extensions.Options;
using LeanKernel.Archivist;
using LeanKernel.Archivist.CapabilityGaps;
using LeanKernel.Archivist.Engagement;
using LeanKernel.Archivist.Embedding;
using LeanKernel.Archivist.Knowledge;
using LeanKernel.Archivist.Sessions;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Commander;
using LeanKernel.Commander.Adapters;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Plugins.BuiltIn;
using LeanKernel.Plugins.BuiltIn.Skills;
using LeanKernel.Scheduler;
using LeanKernel.Thinker;
using LeanKernel.Thinker.Agents;
using LeanKernel.Thinker.Routing;
using LeanKernel.Thinker.Services;
using LeanKernel.Thinker.Workflows;

namespace LeanKernel.Host;

/// <summary>
/// Groups feature-level service registrations so the host composition root stays readable.
/// </summary>
public static class LeanKernelFeatureServiceCollectionExtensions
{
    /// <summary>
    /// Registers Archivist storage, search, embedding, wiki, and context-gating services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddArchivist(this IServiceCollection services)
    {
        services.AddSingleton<IWikiStore, WikiStore>();
        services.AddSingleton<ISessionStore>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
            var sessionsPath = Path.Combine(
                Path.GetDirectoryName(config.Wiki.BasePath) ?? "/app/data",
                "sessions");
            return new SessionStore(sessionsPath, sp.GetRequiredService<ILogger<SessionStore>>());
        });
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddHttpClient<IEmbeddingService, EmbeddingService>((sp, client) =>
        {
            var config = sp.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
            client.BaseAddress = new Uri(config.LiteLlm.BaseUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.LiteLlm.ApiKey}");
        });
        services.AddSingleton<IKnowledgeSearchService, KnowledgeSearchService>();
        services.AddSingleton<WikiCompiler>();
        services.AddSingleton<ConversationCompactor>();
        services.AddSingleton<ICapabilityGapStore, MarkdownCapabilityGapStore>();
        services.AddSingleton<IContextGatekeeper, ContextGatekeeper>();
        services.AddHttpClient<LlmWikiExtractor>((sp, client) =>
        {
            var config = sp.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
            client.BaseAddress = new Uri(config.LiteLlm.BaseUrl);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.LiteLlm.ApiKey}");
        });

        return services;
    }

    /// <summary>
    /// Registers Thinker agent, routing, response enhancement, and self-improvement services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddThinker(this IServiceCollection services)
    {
        services.AddSingleton<LeanKernel.Thinker.Middleware.FunctionLoggingMiddleware>();
        services.AddSingleton<LeanKernel.Thinker.Middleware.DiagnosticsMiddleware>();
        services.AddSingleton<LeanKernel.Thinker.Middleware.ContextGatingMiddleware>();
        services.AddSingleton<AgentFactory>();
        services.AddSingleton<ToolFunctionAdapter>();
        services.AddSingleton<PromptAssembler>();

        services.AddSingleton<KnowledgeEnhancementService>();
        services.AddSingleton<IResponseEnhancer>(sp => sp.GetRequiredService<KnowledgeEnhancementService>());
        services.AddSingleton<IIdentityFileUpdateService, Services.IdentityFileUpdateService>();
        services.AddSingleton<RequestFailureHandler>();
        services.AddSelfImprovement();

        services.AddSingleton<ThinkerServiceDependencies>(sp =>
        {
            var gatekeeper = sp.GetRequiredService<IContextGatekeeper>();
            var sessions = sp.GetRequiredService<ISessionStore>();
            var wiki = sp.GetRequiredService<IWikiStore>();
            var agentFactory = sp.GetRequiredService<AgentFactory>();
            var toolAdapter = sp.GetRequiredService<ToolFunctionAdapter>();
            var promptAssembler = sp.GetRequiredService<PromptAssembler>();
            var responseEnhancer = sp.GetService<IResponseEnhancer>();
            var turnEventSink = sp.GetService<ITurnEventSink>();

            return new ThinkerServiceDependencies(
                gatekeeper, sessions, wiki, agentFactory, toolAdapter, promptAssembler,
                responseEnhancer, turnEventSink);
        });

        services.AddSingleton<TaskComplexityScorer>();
        services.AddSingleton<ProviderHealthTracker>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
            return new ProviderHealthTracker(TimeSpan.FromSeconds(config.Routing.CooldownSeconds));
        });
        services.AddSingleton<SpendGuard>();
        services.AddSingleton<PolicyModelSelector>();
        services.AddSingleton<ResponseQualityGate>();
        services.AddSingleton<SelectionLogStore>();
        services.AddSingleton<ModelRoutingDependencies>();
        services.AddSingleton<ModelRoutingService>();

        services.AddSingleton<IThinkerService, ThinkerService>();
        services.AddSingleton<WorkerAgent, ResearchWorker>();
        services.AddSingleton<WorkerAgent, CodeWorker>();
        services.AddSingleton<WorkerAgent, ScheduleWorker>();
        services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();
        services.AddSingleton<LeanKernelWorkflowBuilder>();

        return services;
    }

    /// <summary>
    /// Registers Commander channels and routing services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddCommander(this IServiceCollection services)
    {
        services.AddHttpClient("signal-daemon", client =>
        {
            // The long-poll endpoint blocks for up to 10 s; use a 60 s timeout so there is
            // plenty of headroom for slow attachment downloads too.
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddSingleton<IChannel, SignalChannel>();
        services.AddSingleton<IChannel>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DiscordChannelAdapter>>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            var config = sp.GetRequiredService<IOptions<LeanKernelConfig>>().Value;

            var botToken = Environment.GetEnvironmentVariable("LEANKERNEL_DISCORD_BOT_TOKEN") ?? config.DiscordBotToken;
            var channelId = Environment.GetEnvironmentVariable("LEANKERNEL_DISCORD_CHANNEL_ID") ?? config.DiscordChannelId;

            return new DiscordChannelAdapter(logger, httpClient, botToken, channelId);
        });
        services.AddSingleton<ChannelRouter>();

        return services;
    }

    /// <summary>
    /// Registers built-in tools and runtime skill loading services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="skillDirectories">The skill directories that should be loaded at startup.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPlugins(this IServiceCollection services, IReadOnlyList<string> skillDirectories)
    {
        services.AddSingleton<ITool, WikiQueryTool>();
        services.AddSingleton<ITool, KnowledgeSearchTool>();
        services.AddSingleton<ITool, FileSystemReadTool>();
        services.AddSingleton<ITool, FileSystemWriteTool>();
        services.AddSingleton<ITool, FileSystemEditTool>();
        services.AddSingleton<ITool, FileSystemDeleteTool>();
        services.AddSingleton<ITool, FileSystemMoveTool>();
        services.AddSingleton<ITool, FileSystemCopyTool>();
        services.AddSingleton<ITool, FileSystemChmodTool>();
        services.AddSingleton<ITool, DirectoryMkdirTool>();
        services.AddSingleton<ITool, FileSystemTouchTool>();
        services.AddSingleton<ITool, DirectoryListTool>();
        services.AddSingleton<ITool, FileSystemStatTool>();

        services.AddMemoryCache();
        services.AddSingleton<SkillParser>();
        services.AddSingleton<IBinaryResolver, BinaryResolver>();
        services.AddSingleton<ISkillRegistry, RuntimeSkillRegistry>();
        services.AddSingleton<DynamicSkillToolFactory>();
        services.AddSingleton<IEnumerable<ISkillLifecycleListener>>(sp => []);
        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<DynamicSkillToolFactory>();
            var builtInTools = sp.GetServices<ITool>();
            var logger = sp.GetRequiredService<ILogger<DynamicPluginHost>>();

            return new DynamicPluginHost(factory, builtInTools, logger);
        });
        services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<DynamicPluginHost>());
        services.AddSingleton(sp =>
        {
            var skillRegistry = sp.GetRequiredService<ISkillRegistry>();
            var pluginHost = sp.GetRequiredService<DynamicPluginHost>();
            var listeners = sp.GetRequiredService<IEnumerable<ISkillLifecycleListener>>();
            var logger = sp.GetRequiredService<ILogger<Services.Skills.SkillHostedService>>();

            return new Services.Skills.SkillHostedService(
                skillRegistry,
                pluginHost,
                listeners,
                logger,
                skillDirectories.Where(Directory.Exists).ToArray());
        });
        services.AddHostedService(sp => sp.GetRequiredService<Services.Skills.SkillHostedService>());

        return services;
    }

    /// <summary>
    /// Registers Scheduler jobs and proactive task execution services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddScheduler(this IServiceCollection services)
    {
        services.AddSingleton<IScheduler, CronScheduler>();
        services.AddSingleton<LeanKernel.Scheduler.Jobs.WikiMaintenanceJob>();
        services.AddSingleton<LeanKernel.Scheduler.Jobs.ChatFactScrubJob>();
        services.AddSingleton<LeanKernel.Scheduler.Jobs.ModelLimitSyncJob>();
        services.AddSingleton<Services.Jobs.UserProfileSyncJob>();
        services.AddSingleton<IAsyncJob>(sp => sp.GetRequiredService<Services.Jobs.UserProfileSyncJob>());
        services.AddSingleton<ProactiveTaskRunner>();

        return services;
    }
}
