using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Tools;

public static class ToolsServiceCollectionExtensions
{
    public static IServiceCollection AddLeanKernelTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ToolGovernancePolicy>();
        services.AddSingleton<IToolRegistry>(serviceProvider =>
        {
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            var policy = serviceProvider.GetRequiredService<ToolGovernancePolicy>();
            var logger = serviceProvider.GetRequiredService<ILogger<ToolRegistry>>();

            IReadOnlyList<ToolDefinition> builtInTools =
            [
                WikiSearchTool.Create(scopeFactory),
                WikiReadTool.Create(scopeFactory),
                WikiWriteTool.Create(scopeFactory)
            ];

            return new ToolRegistry(policy, builtInTools, logger);
        });
        services.AddSingleton<IToolExecutor, ToolExecutor>();

        return services;
    }
}
