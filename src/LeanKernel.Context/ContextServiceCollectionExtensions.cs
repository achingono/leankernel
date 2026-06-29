using System.Net.Http.Headers;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Context.History;
using LeanKernel.Context.Identity;
using LeanKernel.Context.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context;

/// <summary>
/// Provides extension methods for context service collection.
/// </summary>
public static class ContextServiceCollectionExtensions
{
    public static IServiceCollection AddLeanKernelContext(
        this IServiceCollection services,
        LeanKernelConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        return services.AddLeanKernelContext(config.Context, config.Retrieval, config.History, config.LiteLlm);
    }

    public static IServiceCollection AddLeanKernelContext(
        this IServiceCollection services,
        ContextConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        return services.AddLeanKernelContext(
            config,
            new RetrievalConfig(),
            new HistoryConfig
            {
                RecentTurnsVerbatim = config.RecentTurnsVerbatim,
                CompactedTurnsMax = config.CompactedTurnsMax,
                SummarizedTurnsMax = 0,
                EnableCompaction = false,
                EnableSummarization = false,
                PersistCompactionMarkers = false,
            },
            new LiteLlmConfig());
    }

    public static IServiceCollection AddLeanKernelContext(
        this IServiceCollection services,
        ContextConfig contextConfig,
        RetrievalConfig retrievalConfig,
        HistoryConfig historyConfig,
        LiteLlmConfig liteLlmConfig)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(contextConfig);
        ArgumentNullException.ThrowIfNull(retrievalConfig);
        ArgumentNullException.ThrowIfNull(historyConfig);
        ArgumentNullException.ThrowIfNull(liteLlmConfig);

        services.AddSingleton<IOptions<ContextConfig>>(Options.Create(contextConfig));
        services.AddSingleton<IOptions<RetrievalConfig>>(Options.Create(retrievalConfig));
        services.AddSingleton<IOptions<HistoryConfig>>(Options.Create(historyConfig));
        services.AddSingleton<IOptions<LiteLlmConfig>>(Options.Create(liteLlmConfig));
        services.AddSingleton<ITokenEstimator, SimpleTokenEstimator>();
        services.AddHttpClient<IConversationCompactor, ConversationCompactor>((_, client) =>
        {
            client.BaseAddress = new Uri(liteLlmConfig.BaseUrl, UriKind.Absolute);

            if (!string.IsNullOrWhiteSpace(liteLlmConfig.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", liteLlmConfig.ApiKey);
            }
        });
        services.AddSingleton<HistoryCompactionStrategy>();
        services.AddSingleton<HistoryShaper>();
        services.AddScoped<RetrievalScopePolicy>();
        services.AddScoped<EntityExpander>();
        services.AddScoped<IScopedKnowledgeService, ScopedKnowledgeService>();
        services.AddScoped<ContextCandidateRetriever>();
        services.AddSingleton<ConversationHistoryAssembler>();
        services.AddSingleton<PromptAssembler>();
        services.AddScoped<IContextGatekeeper, ContextGatekeeper>();

        return services;
    }

    public static IServiceCollection AddLeanKernelIdentity(
        this IServiceCollection services,
        IdentityConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.AddSingleton<IOptions<IdentityConfig>>(Options.Create(config));
        services.AddScoped<IIdentityProvider, IdentityProvider>();
        services.AddScoped<IOnboardingDetector, OnboardingGapDetector>();
        services.AddSingleton<OnboardingDirectiveBuilder>();
        services.AddSingleton<IdentityUpdateProjector>();

        return services;
    }
}
