using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tools.BuiltIn.Knowledge;

/// <summary>
/// Built-in tool: searches the knowledge wiki via GBrain.
/// </summary>
public static class WikiSearchTool
{
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "wiki_search",
            Description = "Search the knowledge wiki for relevant information",
            Category = "knowledge",
            Parameters =
            [
                new ToolParameter { Name = "query", Type = "string", Description = "Search query", Required = true },
                new ToolParameter { Name = "max_results", Type = "integer", Description = "Maximum results to return", Required = false }
            ],
            Handler = async (args, ct) =>
            {
                var query = ToolArgumentReader.GetString(args, "query");
                var maxResults = ToolArgumentReader.GetInt32OrDefault(args, "max_results", 5);
                if (maxResults <= 0)
                {
                    maxResults = 5;
                }

                if (string.IsNullOrWhiteSpace(query))
                {
                    return new ToolResult { ToolName = "wiki_search", Success = false, Error = "Query is required" };
                }

                using var scope = scopeFactory.CreateScope();
                var knowledge = scope.ServiceProvider.GetRequiredService<IKnowledgeService>();
                var results = await knowledge.SearchAsync(query, maxResults, ct);
                var output = string.Join("\n\n", results.Select(result => $"**{result.Key}** (score: {result.Score:F2})\n{result.Content}"));

                return new ToolResult
                {
                    ToolName = "wiki_search",
                    Success = true,
                    Output = results.Count > 0 ? output : "No results found."
                };
            }
        };
    }
}
