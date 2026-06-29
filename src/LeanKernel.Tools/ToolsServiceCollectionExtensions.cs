using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Data;
using LeanKernel.Tools.BuiltIn.FileSystem;
using LeanKernel.Tools.BuiltIn.Browser;
using LeanKernel.Tools.BuiltIn.Internet;
using LeanKernel.Tools.BuiltIn.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools;

/// <summary>
/// Extension methods for registering tools and related services in the dependency injection container.
/// </summary>
public static class ToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the document library, ingestion services, and all built-in tools into the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection after adding the tools.</returns>
    public static IServiceCollection AddLeanKernelTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<DocumentLibraryService>();
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IOptions<LeanKernelConfig>>().Value.DocumentIngestion;
            return new DocumentIngestionQueue(config?.MaxQueuedDocuments ?? 100);
        });
        services.AddSingleton<IDocumentIngestionQueue>(sp => sp.GetRequiredService<DocumentIngestionQueue>());
        services.AddHostedService<DocumentIngestionHostedService>();
        services.AddHostedService<DocumentFolderIngestionHostedService>();

        services.TryAddSingleton<IWebwrightClient, WebwrightClient>();
        services.AddSingleton<ToolGovernancePolicy>();
        services.AddSingleton<IToolRegistry>(serviceProvider =>
        {
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            var policy = serviceProvider.GetRequiredService<ToolGovernancePolicy>();
            var logger = serviceProvider.GetRequiredService<ILogger<ToolRegistry>>();
            var config = serviceProvider.GetService<IOptions<LeanKernelConfig>>()?.Value ?? new LeanKernelConfig();

            var builtInTools = new List<ToolDefinition>
            {
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
            };

            if (config.Webwright.Enabled)
            {
                builtInTools.Add(BrowserToolDefinitions.CreateRunTaskTool(scopeFactory));
                builtInTools.Add(BrowserToolDefinitions.CreateGetRunTool(scopeFactory));
                builtInTools.Add(BrowserToolDefinitions.CreateGetArtifactTool(scopeFactory));
                builtInTools.Add(BrowserToolDefinitions.CreateCancelRunTool(scopeFactory));
            }

            return new ToolRegistry(policy, builtInTools, logger);
        });
        services.AddSingleton<IToolExecutor, ToolExecutor>();

        return services;
    }
}
