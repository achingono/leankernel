using LeanKernel.Logic.Memory;

using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Logic.Tools.Memory;

/// <summary>
/// Provides the LeanKernel-owned <c>memory_write</c> tool backed by Memory.
/// </summary>
public static class MemoryWriteTool
{
    private const string ToolName = "memory_write";

    /// <summary>
    /// Creates the memory_write tool definition.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>A <see cref="ToolDefinition"/> for memory_write.</returns>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Create or update a knowledge page in Memory",
            Category = "knowledge",
            Parameters =
            [
                new ToolParameter
                {
                    Name = "key",
                    Type = "string",
                    Description = "The page key (slug) to create or update",
                    Required = true
                },
                new ToolParameter
                {
                    Name = "content",
                    Type = "string",
                    Description = "The Markdown content to store in the page",
                    Required = true
                }
            ],
            Handler = async (args, ct) =>
            {
                var key = ToolArgumentReader.GetString(args, "key");
                var content = ToolArgumentReader.GetString(args, "content");

                if (string.IsNullOrWhiteSpace(key))
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = "key is required" };
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = "content is required" };
                }

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var knowledge = scope.ServiceProvider.GetRequiredService<IMemoryService>();

                    await knowledge.PutPageAsync(key, content, ct).ConfigureAwait(false);
                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = true,
                        Output = $"Page '{key}' saved successfully."
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