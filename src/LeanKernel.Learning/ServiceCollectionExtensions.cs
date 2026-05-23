using System.Net.Http.Headers;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Learning;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLeanKernelLearning(this IServiceCollection services, LearningConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        if (!config.Enabled)
        {
            return services;
        }

        services.AddSingleton<IOptions<LearningConfig>>(Options.Create(config));
        services.AddSingleton<KnowledgePageUpdateCoordinator>();
        services.AddSingleton<TurnEventQueue>();
        services.AddSingleton<ITurnEventSink>(provider => provider.GetRequiredService<TurnEventQueue>());
        services.AddSingleton<ISelfImprovementPipeline, SelfImprovementPipeline>();
        services.AddHostedService<LearningBackgroundWorker>();

        if (config.FactExtractionEnabled)
        {
            services.AddHttpClient(FactExtractionStep.HttpClientName, (provider, client) =>
            {
                var resolvedConfig = provider.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
                client.BaseAddress = new Uri(EnsureTrailingSlash(resolvedConfig.LiteLlm.BaseUrl));

                if (!string.IsNullOrWhiteSpace(resolvedConfig.LiteLlm.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resolvedConfig.LiteLlm.ApiKey);
                }
            });
            services.AddSingleton<ILearningStep, FactExtractionStep>();
        }

        if (config.CapabilityGapDetectionEnabled)
        {
            services.AddSingleton<ILearningStep, CapabilityGapDetectionStep>();
        }

        if (config.EngagementTrackingEnabled)
        {
            services.AddSingleton<ILearningStep, EngagementTrackingStep>();
        }

        return services;
    }

    private static string EnsureTrailingSlash(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "http://litellm:4000/"
            : value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
}
