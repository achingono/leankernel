using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.Providers;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;

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
        return services;
    }
}
