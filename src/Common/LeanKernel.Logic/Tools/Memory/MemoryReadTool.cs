using System.Text.Json;

using LeanKernel.Logic.Providers;

using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Logic.Tools.Memory;

/// <summary>
/// Provides the LeanKernel-owned <c>memory_read</c> tool backed by Memory.
/// </summary>
public static class MemoryReadTool
{
    private const string ToolName = "memory_read";

    /// <summary>
    /// Creates the memory_read tool definition.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>A <see cref="ToolDefinition"/> for memory_read.</returns>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Retrieve a knowledge page from Memory by its scope-relative key. Use the ScopeRelativeKey from memory_search results.",
            Category = "knowledge",
            Parameters =
            [
                new ToolParameter
                {
                    Name = "key",
                    Type = "string",
                    Description = "The scope-relative page key to retrieve (use ScopeRelativeKey from search results)",
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
                    var permit = scope.ServiceProvider.GetRequiredService<IPermit>();
                    var memoryClient = scope.ServiceProvider.GetRequiredService<IMemoryClient>();

                    var memoryScope = new MemoryScope
                    {
                        TenantId = permit.TenantId,
                        PersonId = permit.PersonId,
                        ChannelId = permit.ChannelId,
                    };

                    var page = await memoryClient.GetMemoryAsync(memoryScope, key, ct)
                        .ConfigureAwait(false);

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
                        Output = JsonSerializer.Serialize(page, Constants.Serialization.JsonOptions)
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
