using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tools.BuiltIn.Knowledge;

/// <summary>
/// Built-in tool: writes or updates a wiki page in GBrain.
/// </summary>
public static class WikiWriteTool
{
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = "wiki_write",
            Description = "Create or update a page in the knowledge wiki",
            Category = "knowledge",
            Parameters =
            [
                new ToolParameter { Name = "key", Type = "string", Description = "Page key/path to write", Required = true },
                new ToolParameter { Name = "content", Type = "string", Description = "Page content (markdown)", Required = true }
            ],
            Handler = async (args, ct) =>
            {
                var key = ToolArgumentReader.GetString(args, "key");
                var content = ToolArgumentReader.GetString(args, "content");

                if (string.IsNullOrWhiteSpace(key))
                {
                    return new ToolResult { ToolName = "wiki_write", Success = false, Error = "Key is required" };
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    return new ToolResult { ToolName = "wiki_write", Success = false, Error = "Content is required" };
                }

                using var scope = scopeFactory.CreateScope();
                var knowledge = scope.ServiceProvider.GetRequiredService<IKnowledgeService>();
                await knowledge.PutPageAsync(key, content, ct);

                return new ToolResult
                {
                    ToolName = "wiki_write",
                    Success = true,
                    Output = $"Page '{key}' updated successfully."
                };
            }
        };
    }
}
