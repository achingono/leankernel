using System.Text.Json;

using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Logic.Providers;

using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Provides the <c>document_list</c> tool backed by the document store client.
/// Resolves identity and channel visibility from the invocation scope.
/// </summary>
public static class DocumentListTool
{
    private const string ToolName = "document_list";

    /// <summary>
    /// Creates the document list tool definition.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <returns>A <see cref="ToolDefinition"/> for document_list.</returns>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "List ingested documents, optionally filtered by channel",
            Category = "documents",
            Parameters =
            [
                new ToolParameter
                {
                    Name = "channelIds",
                    Type = "string",
                    Description = "Optional comma-separated channel identifiers to filter by",
                    Required = false
                },
                new ToolParameter
                {
                    Name = "limit",
                    Type = "integer",
                    Description = "Maximum number of results (default 50)",
                    Required = false
                }
            ],
            Handler = async (args, ct) =>
            {
                var limit = ToolArgumentReader.GetInt(args, "limit") ?? 50;

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
                                Error = $"Not authorized to list channel(s): {string.Join(", ", unauthorized)}"
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

                    var results = await store.ListAsync(scopeCtx, searchChannelIds ?? readableChannels, limit, ct);
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
