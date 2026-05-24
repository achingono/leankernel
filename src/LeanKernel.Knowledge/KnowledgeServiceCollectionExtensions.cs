using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Knowledge;

public static class KnowledgeServiceCollectionExtensions
{
    public static IServiceCollection AddLeanKernelKnowledge(this IServiceCollection services, GBrainConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.AddSingleton<IOptions<GBrainConfig>>(Options.Create(config));
        services.AddTransient<GBrainAuthHandler>();
        services.AddHttpClient<GBrainMcpClient>(client =>
        {
            var baseUrl = config.BaseUrl.TrimEnd('/');
            client.BaseAddress = new Uri($"{baseUrl}/mcp");
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        })
        .AddHttpMessageHandler<GBrainAuthHandler>();

        services.AddSingleton<GBrainKnowledgeService>();
        services.AddSingleton<IKnowledgeService>(provider => provider.GetRequiredService<GBrainKnowledgeService>());

        return services;
    }
}
