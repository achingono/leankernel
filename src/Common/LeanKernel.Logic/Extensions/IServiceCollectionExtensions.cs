using System.ClientModel;
using LeanKernel.Logic;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Microsoft.Extensions.DependencyInjection;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddContextProviders(this IServiceCollection services)
    {
        services.AddScoped<AIContextProvider>();
        return services;
    }

    public static IServiceCollection AddChatHistoryProviders(this IServiceCollection services)
    {
        services.AddScoped<DbChatHistoryProvider>();
        return services;
    }

    public static IServiceCollection AddChatClient(this IServiceCollection services)
    {
        services.AddScoped<IChatClient>(provider =>
        {
            var _config = provider.GetRequiredService<IOptions<OpenAISettings>>().Value;
            var client = new OpenAIClient(
            new ApiKeyCredential(_config.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(_config.BaseUrl) });

            return client.GetChatClient(_config.DefaultModel).AsIChatClient();
        });
        return services;
    }

}