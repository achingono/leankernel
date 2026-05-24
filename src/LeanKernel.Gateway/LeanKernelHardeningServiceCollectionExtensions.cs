using System.Net.Http.Headers;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Agents.Health;
using LeanKernel.Agents.Resilience;
using LeanKernel.Diagnostics.Health;
using LeanKernel.Diagnostics.SpendGuard;
using LeanKernel.Gateway.Middleware;
using LeanKernel.Knowledge;
using LeanKernel.Knowledge.Health;
using LeanKernel.Knowledge.Resilience;
using LeanKernel.Persistence;
using LeanKernel.Persistence.Health;
using LeanKernel.Persistence.Resilience;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway;

/// <summary>
/// Registers LeanKernel production-hardening services.
/// </summary>
public static class LeanKernelHardeningServiceCollectionExtensions
{
    /// <summary>
    /// Registers production-hardening services for LeanKernel.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="config">The hardening configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddLeanKernelHardening(this IServiceCollection services, HardeningConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOptions<HardeningConfig>>(Options.Create(config));
        services.AddHttpContextAccessor();
        services.AddTransient<CorrelationIdDelegatingHandler>();
        services.AddSingleton<DegradedSessionBuffer>();
        services.AddSingleton<ISpendTracker, SpendTracker>();
        services.AddSingleton<ISpendGuardService, SpendGuardService>();
        services.AddSingleton<ProviderHealthTracker>();
        services.AddSingleton<IProviderHealthTracker>(provider => provider.GetRequiredService<ProviderHealthTracker>());
        services.AddHostedService(provider => provider.GetRequiredService<ProviderHealthTracker>());
        services.AddSingleton<IGracefulDegradationPolicy, GracefulDegradationPolicy>();
        services.AddSingleton<IProviderHealthProbe, DatabaseHealthProbe>();
        services.AddSingleton<IProviderHealthProbe, LiteLlmHealthProbe>();
        services.AddSingleton<IProviderHealthProbe, GBrainHealthProbe>();
        services.AddHealthChecks().AddCheck<ProviderHealthCheck>("providers");

        services.AddHttpClient(LiteLlmHealthProbe.HttpClientName, (provider, client) =>
        {
            var resolvedConfig = provider.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
            client.BaseAddress = new Uri(EnsureTrailingSlash(resolvedConfig.LiteLlm.BaseUrl), UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, resolvedConfig.Hardening.Resilience.TimeoutSeconds));

            if (!string.IsNullOrWhiteSpace(resolvedConfig.LiteLlm.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resolvedConfig.LiteLlm.ApiKey);
            }
        });

        services.AddHttpClient(GBrainHealthProbe.HttpClientName, (provider, client) =>
        {
            var resolvedConfig = provider.GetRequiredService<IOptions<GBrainConfig>>().Value;
            client.BaseAddress = new Uri(new Uri(resolvedConfig.BaseUrl, UriKind.Absolute), "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, resolvedConfig.TimeoutSeconds));

            if (!string.IsNullOrWhiteSpace(resolvedConfig.AuthToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resolvedConfig.AuthToken);
            }
        });

        services.ConfigureAll<HttpClientFactoryOptions>(options =>
        {
            options.HttpMessageHandlerBuilderActions.Add(builder =>
            {
                builder.AdditionalHandlers.Add(builder.Services.GetRequiredService<CorrelationIdDelegatingHandler>());
            });
        });

        services.AddScoped<ISessionStore>(provider => new ResilientSessionStore(
            provider.GetRequiredService<PostgresSessionStore>(),
            provider.GetRequiredService<DegradedSessionBuffer>(),
            provider.GetRequiredService<ILogger<ResilientSessionStore>>(),
            provider.GetService<IProviderHealthTracker>()));

        services.AddSingleton<IKnowledgeService>(provider => new ResilientKnowledgeService(
            provider.GetRequiredService<GBrainKnowledgeService>(),
            provider.GetRequiredService<ILogger<ResilientKnowledgeService>>(),
            provider.GetService<IProviderHealthTracker>()));

        return services;
    }

    private static string EnsureTrailingSlash(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "http://litellm:4000/"
            : value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
}
