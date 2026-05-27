using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Data;
using LeanKernel.Tools.BuiltIn.FileSystem;
using LeanKernel.Tools.BuiltIn.Internet;
using LeanKernel.Tools.BuiltIn.Knowledge;
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
                DirectoryCreateTool.Create(scopeFactory),
                DirectoryListTool.Create(scopeFactory),
                ExtractTextTool.Create(scopeFactory),
                FileReadTool.Create(scopeFactory),
                FileWriteTool.Create(scopeFactory),
                FileEditTool.Create(scopeFactory),
                FileCopyTool.Create(scopeFactory),
                FileMoveTool.Create(scopeFactory),
                FileDeleteTool.Create(scopeFactory),
                FileSearchTool.Create(scopeFactory),
                FileStatTool.Create(scopeFactory),
                FileTouchTool.Create(scopeFactory),
                FileChmodTool.Create(scopeFactory),
                JsonTransformTool.Create(scopeFactory),
                CsvXlsxReadWriteTool.Create(scopeFactory),
                DatabaseQueryTool.Create(scopeFactory),
                WikiSearchTool.Create(scopeFactory),
                WikiReadTool.Create(scopeFactory),
                WikiWriteTool.Create(scopeFactory),
                WebSearchTool.Create(scopeFactory),
                WebFetchTool.Create(scopeFactory),
                HttpRequestTool.Create(scopeFactory)
            ];

            return new ToolRegistry(policy, builtInTools, logger);
        });
        services.AddSingleton<IToolExecutor, ToolExecutor>();

        return services;
    }
}
