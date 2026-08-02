using LeanKernel;
using LeanKernel.Logic.Mcp;
using LeanKernel.Logic.Tools;

namespace Microsoft.Extensions.DependencyInjection;

using LeanKernel.Services.Gateway.Providers;

/// <summary>
/// Provides LeanKernel gateway service registration extensions.
/// </summary>
public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the generic <see cref="IPermit{TEntity}"/> open-generic service
    /// backed by <see cref="RequestContextPermit{TEntity}"/>.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPermits(this IServiceCollection services)
    {
        services.AddScoped(typeof(IPermit<>), typeof(RequestContextPermit<>));
        return services;
    }

    /// <summary>
    /// Registers the shared tool registry as a singleton.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddToolRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IMcpToolProvider, McpToolProvider>();
        services.AddSingleton<IProviderHealthProbe, McpServersHealthProbe>();

        // Named HTTP clients for tool egress
        services.AddHttpClient("web-search");
        services.AddHttpClient("dynamic-skill");

        // Named HTTP client for chat completions proxy with extended timeout
        // The internal MAF handler can take longer than the default 100s HttpClient timeout
        services.AddHttpClient(Constants.HttpClientNames.ChatCompletionsProxy)
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(5));

        return services;
    }
}
