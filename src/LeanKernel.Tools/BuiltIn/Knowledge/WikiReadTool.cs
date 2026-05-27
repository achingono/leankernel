using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tools.BuiltIn.Knowledge;

/// <summary>
/// Built-in tool: reads a specific wiki page from GBrain.
/// </summary>
public static class WikiReadTool
{
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "wiki_read",
            Description = "Read a specific page from the knowledge wiki",
            Category = "knowledge",
            Parameters =
            [
                new ToolParameter { Name = "key", Type = "string", Description = "Page key/path to read", Required = true }
            ],
            Handler = async (args, ct) =>
            {
                var key = ToolArgumentReader.GetString(args, "key");

                if (string.IsNullOrWhiteSpace(key))
                {
                    return new ToolResult { ToolName = "wiki_read", Success = false, Error = "Key is required" };
                }

                using var scope = scopeFactory.CreateScope();
                var knowledge = scope.ServiceProvider.GetRequiredService<IKnowledgeService>();
                var page = await knowledge.GetPageAsync(key, ct);
                if (page is null)
                {
                    return new ToolResult { ToolName = "wiki_read", Success = false, Error = $"Page '{key}' not found" };
                }

                return new ToolResult
                {
                    ToolName = "wiki_read",
                    Success = true,
                    Output = page.Content
                };
            }
        };
    }
}
