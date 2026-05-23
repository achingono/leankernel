using System.Net.Http.Headers;
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
        services.AddHttpClient<GBrainMcpClient>(client =>
        {
            client.BaseAddress = new Uri(config.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

            if (!string.IsNullOrWhiteSpace(config.AuthToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.AuthToken);
            }
        });

        services.AddSingleton<GBrainKnowledgeService>();
        services.AddSingleton<IKnowledgeService>(provider => provider.GetRequiredService<GBrainKnowledgeService>());

        return services;
    }
}
