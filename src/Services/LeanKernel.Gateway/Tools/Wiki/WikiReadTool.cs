using System.Text.Json;
using LeanKernel.Gateway.Providers;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Gateway.Tools.Wiki;

/// <summary>
/// Provides the LeanKernel-owned <c>wiki_read</c> tool backed by GBrain.
/// </summary>
public static class WikiReadTool
{
    private const string ToolName = "wiki_read";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates the wiki_read tool definition.
    /// </summary>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Retrieve a knowledge page from GBrain by its key",
            Category = "knowledge",
            Parameters =
            [
                new ToolParameter
                {
                    Name = "key",
                    Type = "string",
                    Description = "The page key (slug) to retrieve",
                    Required = true
                }
            ],
            Handler = async (args, ct) =>
            {
                var key = ToolArgumentReader.GetString(args, "key");
                if (string.IsNullOrWhiteSpace(key))
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = "key is required" };
                }

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var knowledge = scope.ServiceProvider.GetRequiredService<IKnowledgeService>();

                    var page = await knowledge.GetPageAsync(key, ct).ConfigureAwait(false);
                    if (page is null)
                    {
                        return new ToolResult
                        {
                            ToolName = ToolName,
                            Success = false,
                            Error = $"Page not found: {key}"
                        };
                    }

                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = true,
                        Output = JsonSerializer.Serialize(page, JsonOptions)
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
