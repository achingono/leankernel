using System.Text.Json;
using LeanKernel.Logic.Memory;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Logic.Tools.Memory;

/// <summary>
/// Provides the LeanKernel-owned <c>memory_read</c> tool backed by Memory.
/// </summary>
public static class MemoryReadTool
{
    private const string ToolName = "memory_read";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates the memory_read tool definition.
    /// </summary>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Retrieve a knowledge page from Memory by its key",
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
                    var memoryService = scope.ServiceProvider.GetRequiredService<IMemoryService>();

                    var page = await memoryService.GetPageAsync(key, ct).ConfigureAwait(false);
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
