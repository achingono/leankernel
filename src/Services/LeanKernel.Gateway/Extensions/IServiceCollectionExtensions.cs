using LeanKernel.Data;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.HealthChecks;
using LeanKernel.Gateway.Memory;
using LeanKernel.Gateway.Providers;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
using LeanKernel.Logic.Providers;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides LeanKernel gateway service registration extensions.
/// </summary>
public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GBrain-backed memory client and its supporting services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="config">The GBrain configuration to bind into the registered services.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddGBrainMemory(
        this IServiceCollection services,
        GBrainSettings config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.Configure<GBrainSettings>(opts =>
        {
            opts.BaseUrl = config.BaseUrl;
            opts.AuthToken = config.AuthToken;
            opts.TimeoutSeconds = config.TimeoutSeconds;
        });

        services.AddTransient<GBrainAuthHandler>();
        services.AddHttpClient<GBrainMcpClient>(client =>
        {
            var baseUrl = config.BaseUrl.TrimEnd('/');
            client.BaseAddress = new Uri($"{baseUrl}/mcp");
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        })
        .AddHttpMessageHandler<GBrainAuthHandler>();
        services.AddScoped<IGBrainMcpClient>(sp => sp.GetRequiredService<GBrainMcpClient>());
        services.AddScoped<IMemoryClient, GBrainMemoryClient>();

        // GBrain knowledge service for callable wiki tools
        services.AddScoped<IMemoryService, GBrainService>();

        // Capability pre-check (transient — used once at startup)
        services.AddTransient<IMemoryCapabilityCheck, GBrainCapabilityCheck>();

        return services;
    }

    /// <summary>
    /// Registers the shared tool registry as a singleton.
    /// </summary>
    public static IServiceCollection AddToolRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IToolRegistry, ToolRegistry>();

        // Named HTTP clients for tool egress
        services.AddHttpClient("web-search");
        services.AddHttpClient("dynamic-skill");

        return services;
    }

    /// <summary>
    /// Registers health checks for the gateway's dependent services:
    /// the EF Core database, LiteLLM proxy, and GBrain MCP service.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The <see cref="IHealthChecksBuilder"/> for further configuration.</returns>
    public static IHealthChecksBuilder AddGatewayHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var probeTimeout = TimeSpan.FromSeconds(5);

        services.AddHttpClient(LiteLlmHealthCheck.HttpClientName)
            .ConfigureHttpClient(c => c.Timeout = probeTimeout);

        services.AddHttpClient(GBrainHealthCheck.HttpClientName)
            .ConfigureHttpClient(c => c.Timeout = probeTimeout);

        return services.AddHealthChecks()
            .AddDbContextCheck<EntityContext>("database", tags: ["database"])
            .AddCheck<LiteLlmHealthCheck>("litellm", tags: ["litellm"])
            .AddCheck<GBrainHealthCheck>("gbrain", tags: ["gbrain"]);
    }
}
