using System.ClientModel;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Microsoft.Extensions.DependencyInjection;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers chat history and memory providers scoped by identity, against base types.
    /// </summary>
    public static IServiceCollection AddContextProviders(this IServiceCollection services)
    {
        services.AddScoped<ChatHistoryProvider, DbChatHistoryProvider>();
        services.AddScoped<AIContextProvider, MemoryProvider>();
        return services;
    }

    /// <summary>
    /// Registers the OpenAI-compatible chat client from configuration.
    /// </summary>
    public static IServiceCollection AddLeanKernelChatClient(this IServiceCollection services)
    {
        services.AddChatClient(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<OpenAISettings>>().Value;
            var client = new OpenAIClient(
                new ApiKeyCredential(cfg.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(cfg.BaseUrl) });
            return client.GetChatClient(cfg.DefaultModel).AsIChatClient();
        })
        .UseFunctionInvocation()
        .UseLogging();
        return services;
    }

    /// <summary>
    /// Registers a named <see cref="AIAgent"/> as a singleton using MAF hosting primitives.
    /// </summary>
    public static IServiceCollection AddLeanKernelAgent(
        this IServiceCollection services,
        string agentName,
        IConfiguration configuration)
    {
        services.Configure<AgentSettings>(configuration.GetSection("Agents"));

        services.AddAIAgent(agentName, (sp, name) =>
        {
            var settings = sp.GetRequiredService<IOptions<AgentSettings>>().Value;
            return new ChatClientAgent(
                sp.GetRequiredService<IChatClient>(),
                new ChatClientAgentOptions
                {
                    Name = name,
                    Description = settings.DefaultDescription,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = settings.DefaultInstructions,
                    },
                    ChatHistoryProvider = sp.GetRequiredService<ChatHistoryProvider>(),
                    AIContextProviders = sp.GetServices<AIContextProvider>().ToList(),
                },
                sp.GetRequiredService<ILoggerFactory>(),
                sp);
        }, ServiceLifetime.Singleton);

        return services;
    }
}
