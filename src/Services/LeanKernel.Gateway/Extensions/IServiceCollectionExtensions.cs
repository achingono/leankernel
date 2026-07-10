using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class IServiceCollectionExtensions
{

    public static IServiceCollection AddAgent(this IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var settings = configuration.GetSection("Agents").Get<LeanKernel.Gateway.Configuration.AgentSettings>();

        if (settings == null || string.IsNullOrWhiteSpace(settings.RootPath))
            throw new InvalidOperationException("AgentSettings section is missing in the configuration.");

        // check if the agent root path exists
        if (!Directory.Exists(settings.RootPath))
            Directory.CreateDirectory(settings.RootPath);

        // Get the us

        services.TryAddScoped<AIAgent>(
            provider =>
            {
                var chatClient = provider.GetRequiredService<IChatClient>();
                return new ChatClientAgent(
                    chatClient,
                    options: new ChatClientAgentOptions
                    {
                        Name = settings.DefaultName,
                        Description = settings.DefaultDescription
                    },
                    provider.GetRequiredService<ILoggerFactory>(),
                    provider
                );
            }
        );
        return services;
    }
}