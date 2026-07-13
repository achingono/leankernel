using System.ClientModel;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
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
        services.AddMemoryPageServices();
        return services;
    }

    public static IServiceCollection AddMemoryPageServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MemoryPageParser>();
        services.AddScoped<MemoryPageRenderer>();
        services.AddScoped<MemoryPageNormalizer>();
        services.AddScoped<MemoryDimensionClassifier>();
        services.AddScoped<MemoryPageLinker>();
        services.AddScoped<MemoryGraphReasoner>();
        services.AddScoped<MemoryFieldRepairService>();
        services.AddScoped<MemoryPageKeyBuilder>();
        services.AddScoped<FactExtractionService>();

        services.AddScoped<IReasoningModel>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<MemorySettings>>().Value;
            var logger = sp.GetRequiredService<ILogger<ReasoningModel>>();
            var chatClient = sp.GetRequiredKeyedService<IChatClient>("small-model");
            return new ReasoningModel(chatClient, cfg, logger);
        });

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

        services.AddKeyedScoped<IChatClient>("small-model", (sp, _) =>
        {
            var cfg = sp.GetRequiredService<IOptions<MemorySettings>>().Value;
            if (!cfg.Enabled)
            {
                return new DisabledChatClient();
            }

            var openAi = sp.GetRequiredService<IOptions<OpenAISettings>>().Value;
            var client = new OpenAIClient(
                new ApiKeyCredential(openAi.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(openAi.BaseUrl) });
            return client.GetChatClient(cfg.ModelId).AsIChatClient();
        });

        services.AddKeyedScoped<IChatClient>("fact-extraction", (sp, _) =>
        {
            var cfg = sp.GetRequiredService<IOptions<FactExtractionSettings>>().Value;
            var openAi = sp.GetRequiredService<IOptions<OpenAISettings>>().Value;
            var client = new OpenAIClient(
                new ApiKeyCredential(openAi.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(openAi.BaseUrl) });
            return client.GetChatClient(cfg.ModelId).AsIChatClient();
        });

        return services;
    }

    /// <summary>
    /// Registers a named <see cref="AIAgent"/> as scoped using MAF hosting primitives.
    /// Scoped lifetime ensures providers (ChatHistoryProvider, AIContextProvider) are resolved
    /// from the request scope rather than captured at singleton creation time.
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
        }, ServiceLifetime.Scoped);

        return services;
    }

}
