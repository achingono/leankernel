using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Retrieves memory/knowledge candidates scoped by tenant/user/channel from
/// <see cref="IMemoryClient"/> and adds them to <see cref="TurnContext.Candidates"/>
/// before admission gating. Runs before <see cref="ContextGatekeeper"/>.
/// </summary>
public sealed class ScopedRetrievalStage(
    IMemoryClient memoryClient,
    IOptions<TurnPipelineSettings> settings,
    ILogger<ScopedRetrievalStage> logger) : ITurnStage
{
    private const int CharsPerTokenEstimate = 4;
    private readonly TurnPipelineSettings _settings = settings.Value;

    /// <inheritdoc />
    public string Name => "ScopedRetrieval";

    /// <inheritdoc />
    public async Task ExecuteAsync(TurnContext context, CancellationToken cancellationToken = default)
    {
        var permit = context.Permit;
        var query = context.UserMessage;

        if (string.IsNullOrWhiteSpace(query))
        {
            logger.LogDebug("No user message for retrieval; skipping scoped retrieval.");
            return;
        }

        var scope = new MemoryScope
        {
            TenantId = permit.TenantId,
            UserId = permit.UserId,
            ChannelId = permit.ChannelId,
        };

        try
        {
            var memories = await memoryClient.SearchMemoriesAsync(
                scope, query, _settings.MaxRetrievalCandidates, cancellationToken).ConfigureAwait(false);

            if (memories.Count == 0)
            {
                logger.LogDebug(
                    "No memory candidates returned for scope (tenant={TenantId}, user={UserId}, channel={ChannelId}).",
                    scope.TenantId, scope.UserId, scope.ChannelId);
                return;
            }

            foreach (var memory in memories)
            {
                var content = string.IsNullOrWhiteSpace(memory.Text)
                    ? memory.Key
                    : memory.Text;

                context.Candidates.Add(new ContextItem
                {
                    Source = "memory",
                    Content = content,
                    EstimatedTokens = EstimateTokens(content),
                    Score = memory.Score,
                    Metadata = new Dictionary<string, string>
                    {
                        ["memory_key"] = memory.Key,
                        ["tenant_id"] = scope.TenantId.ToString(),
                        ["user_id"] = scope.UserId.ToString(),
                        ["channel_id"] = scope.ChannelId.ToString(),
                    },
                });
            }

            logger.LogDebug(
                "Scoped retrieval added {Count} memory candidates for scope (tenant={TenantId}, user={UserId}, channel={ChannelId}).",
                memories.Count, scope.TenantId, scope.UserId, scope.ChannelId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Scoped retrieval failed for scope (tenant={TenantId}, user={UserId}, channel={ChannelId}); continuing without memory context.",
                scope.TenantId, scope.UserId, scope.ChannelId);
        }
    }

    private static int EstimateTokens(string text)
        => Math.Max(1, text.Length / CharsPerTokenEstimate);
}
