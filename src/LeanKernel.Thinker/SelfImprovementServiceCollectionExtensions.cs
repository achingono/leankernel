using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Thinker.Services;

namespace LeanKernel.Thinker;

/// <summary>
/// Registers the always-on self-improvement pipeline.
/// </summary>
public static class SelfImprovementServiceCollectionExtensions
{
    /// <summary>
    /// Registers the turn-event queue, worker, pipeline, and default learning steps.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSelfImprovement(this IServiceCollection services)
    {
        services.AddSingleton<TurnEventQueue>();
        services.AddSingleton<ITurnEventSink>(sp => sp.GetRequiredService<TurnEventQueue>());
        services.AddSingleton<ISelfImprovementPipeline, SelfImprovementPipeline>();

        services.AddSingleton<ILearningStep>(sp =>
            IsEnabled(sp, c => c.LlmExtractionEnabled) && sp.GetService<LlmWikiExtractor>() is not null
                ? ActivatorUtilities.CreateInstance<LlmFactExtractionStep>(sp)
                : new NullLearningStep("llm-fact-extraction"));
        services.AddSingleton<ILearningStep>(sp =>
            IsEnabled(sp, c => c.IdentityRefreshEnabled) && sp.GetService<IIdentityFileUpdateService>() is not null
                ? ActivatorUtilities.CreateInstance<IdentityRefreshStep>(sp)
                : new NullLearningStep("identity-refresh"));
        services.AddSingleton<ILearningStep>(sp =>
            IsEnabled(sp, c => c.FailureRecoveryEnabled) && sp.GetService<RequestFailureHandler>() is not null
                ? ActivatorUtilities.CreateInstance<FailureRecoveryStep>(sp)
                : new NullLearningStep("failure-recovery"));

        services.AddHostedService<SelfImprovementWorker>();
        return services;
    }

    private static bool IsEnabled(IServiceProvider services, Func<SelfImprovementConfig, bool> selector)
    {
        var config = services.GetRequiredService<IOptions<LeanKernelConfig>>().Value.SelfImprovement;
        return config.Enabled && selector(config);
    }
}
