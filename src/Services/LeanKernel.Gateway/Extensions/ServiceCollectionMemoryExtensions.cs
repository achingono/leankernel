using LeanKernel.Gateway.Providers;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionMemoryExtensions
{
    public static IServiceCollection AddLeanKernelKnowledge(
        this IServiceCollection services,
        GBrainConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.Configure<GBrainConfig>(opts =>
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
