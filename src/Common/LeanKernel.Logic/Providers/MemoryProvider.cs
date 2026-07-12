using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace LeanKernel.Logic.Providers;

/// <summary>
/// Provides AI context (memory retrieval) backed by GBrain via <see cref="IMemoryClient"/>,
/// scoped by tenant/user/channel identity.
/// </summary>
public class MemoryProvider(IMemoryClient memoryClient, IPermit permit) : AIContextProvider
{
    private const int MaxMemoryResults = 10;

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var queryText = string.Join("\n", context.AIContext.Messages?.Select(x => x.Text) ?? []);

        if (string.IsNullOrWhiteSpace(queryText))
            return new AIContext { Messages = [] };

        var scope = new MemoryScope
        {
            TenantId = permit.TenantId,
            UserId = permit.UserId,
            ChannelId = permit.ChannelId
        };

        try
        {
            var memories = await memoryClient.SearchMemoriesAsync(
                scope, queryText, MaxMemoryResults, cancellationToken);

            if (memories.Count == 0)
                return new AIContext { Messages = [] };

            var admitted = memories
                .OrderByDescending(m => m.Score)
                .Take(MaxMemoryResults)
                .ToList();

            var memoryText = string.Join("\n---\n", admitted.Select(m => m.Text));

            return new AIContext
            {
                Messages =
                [
                    new ChatMessage(ChatRole.User,
                        "Here are some memories to help answer the user question:\n```\n" + memoryText + "\n```")
                ]
            };
        }
        catch
        {
            // Degrade gracefully when memory service is unavailable
            return new AIContext { Messages = [] };
        }
    }

    protected override ValueTask StoreAIContextAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        // Phase 1: no-op writeback
        return ValueTask.CompletedTask;
    }
}
