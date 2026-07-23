using System.Text.Json;

using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Logic.Providers;

using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Provides the <c>document_search</c> tool backed by the document store client.
/// Resolves identity and channel visibility from the invocation scope.
/// </summary>
public static class DocumentSearchTool
{
    private const string ToolName = "document_search";

    /// <summary>
    /// Creates the document search tool definition.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>A <see cref="ToolDefinition"/> for document_search.</returns>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Search ingested documents by query text",
            Category = "documents",
            Parameters =
            [
                new ToolParameter
                {
                    Name = "query",
                    Type = "string",
                    Description = "The search query text",
                    Required = true
                },
                new ToolParameter
                {
                    Name = "channelIds",
                    Type = "string",
                    Description = "Optional comma-separated channel identifiers to filter by",
                    Required = false
                },
                new ToolParameter
                {
                    Name = "maxResults",
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

                var maxResults = ToolArgumentReader.GetInt(args, "maxResults") ?? 10;

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var sp = scope.ServiceProvider;
                    var store = sp.GetRequiredService<IDocumentStoreClient>();
                    var permit = sp.GetRequiredService<IPermit>();
                    var policyResolver = sp.GetRequiredService<IChannelMemoryPolicyResolver>();

                    var policy = await policyResolver.ResolveAsync(permit.TenantId, permit.ChannelId, ct);
                    var readableChannels = policy.ReadableChannelIds
                        .Append(permit.ChannelId)
                        .Distinct()
                        .ToList();

                    List<Guid>? searchChannelIds = null;
                    var channelIdsArg = ToolArgumentReader.GetString(args, "channelIds");
                    if (!string.IsNullOrWhiteSpace(channelIdsArg))
                    {
                        var parsed = channelIdsArg
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(Guid.Parse)
                            .ToList();

                        var unauthorized = parsed.Except(readableChannels).ToList();
                        if (unauthorized.Count > 0)
                        {
                            return new ToolResult
                            {
                                ToolName = ToolName,
                                Success = false,
                                Error = $"Not authorized to search channel(s): {string.Join(", ", unauthorized)}"
                            };
                        }

                        searchChannelIds = parsed;
                    }

                    var scopeCtx = new DocumentScopeContext(
                        permit.TenantId,
                        permit.UserId,
                        permit.PersonId,
                        permit.ChannelId,
                        DocumentAvailabilityScope.Channel);

                    var results = await store.SearchAsync(scopeCtx, query, searchChannelIds ?? readableChannels, maxResults, ct);
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
