using System.Text.Json;

using LeanKernel.Logic.Providers;

using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Logic.Tools.Memory;

/// <summary>
/// Provides the LeanKernel-owned <c>memory_search</c> tool backed by Memory.
/// </summary>
public static class MemorySearchTool
{
    private const string ToolName = "memory_search";

    /// <summary>
    /// Creates the memory_search tool definition.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>A <see cref="ToolDefinition"/> for memory_search.</returns>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Search Memory knowledge pages for documents matching the given query. Results are scoped to the current identity.",
            Category = "knowledge",
            Parameters =
            [
                new ToolParameter
                {
                    Name = "query",
                    Type = "string",
                    Description = "The search query",
                    Required = true
                },
                new ToolParameter
                {
                    Name = "limit",
                    Type = "integer",
                    Description = "Maximum number of results (default 10)",
                    Required = false
                }
            ],
            Handler = async (args, ct) =>
            {
                var query = ToolArgumentReader.GetString(args, "query");
                if (string.IsNullOrWhiteSpace(query))
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = "query is required" };
                }

                var limit = ToolArgumentReader.GetInt(args, "limit") ?? 10;

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var permit = scope.ServiceProvider.GetRequiredService<IPermit>();
                    var memoryClient = scope.ServiceProvider.GetRequiredService<IMemoryClient>();

                    var memoryScope = new MemoryScope
                    {
                        TenantId = permit.TenantId,
                        PersonId = permit.PersonId,
                        ChannelId = permit.ChannelId,
                    };

                    var results = await memoryClient.SearchMemoriesAsync(memoryScope, query, limit, ct)
                        .ConfigureAwait(false);

                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = true,
                        Output = JsonSerializer.Serialize(results, Constants.Serialization.JsonOptions)
                    };
                }
                catch (Exception ex)
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = ex.Message };
                }
            }
        };
    }
}