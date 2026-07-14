using System.Text.Json;
using LeanKernel.Gateway.Providers;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Gateway.Tools.Wiki;

/// <summary>
/// Provides the LeanKernel-owned <c>wiki_search</c> tool backed by GBrain.
/// </summary>
public static class WikiSearchTool
{
    private const string ToolName = "wiki_search";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates the wiki_search tool definition.
    /// </summary>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Search GBrain knowledge pages for documents matching the given query",
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
                    var knowledge = scope.ServiceProvider.GetRequiredService<IKnowledgeService>();

                    var results = await knowledge.SearchAsync(query, limit, ct).ConfigureAwait(false);
                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = true,
                        Output = JsonSerializer.Serialize(results, JsonOptions)
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
