using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using LeanKernel.Logic.Memory;

namespace LeanKernel.Logic.Providers;

/// <summary>
/// Provides AI context (memory retrieval) backed by GBrain via <see cref="IMemoryClient"/>,
/// scoped by tenant/user/channel identity.
/// </summary>
public class MemoryProvider(
    IMemoryClient memoryClient,
    IPermit permit,
    MemoryPageParser parser,
    MemoryPageRenderer renderer,
    MemoryPageNormalizer normalizer,
    FactExtractionService factExtractionService,
    TimeProvider timeProvider,
    ILogger<MemoryProvider> logger) : AIContextProvider
{
    private const int MaxMemoryResults = 10;

    /// <inheritdoc />
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

            var memoryText = string.Join("\n", admitted.Select(m => BuildCompactSummary(m.Key, m.Text)));

            return new AIContext
            {
                Messages =
                [
                    new ChatMessage(ChatRole.User,
                        "Here are some memories to help answer the user question:\n```\n" + memoryText + "\n```")
                ]
            };
        }
        catch (Exception ex)
        {
            // Degrade gracefully when memory service is unavailable
            logger.LogWarning(ex, "Memory search failed for scope (tenant={TenantId}, user={UserId}, channel={ChannelId}); continuing without memory context.",
                scope.TenantId, scope.UserId, scope.ChannelId);
            return new AIContext { Messages = [] };
        }
    }

    /// <inheritdoc />
    protected override ValueTask StoreAIContextAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask(StoreCoreAsync(context, cancellationToken));
    }

    /// <summary>
    /// Extracts, normalizes, and persists new facts from the latest invocation.
    /// </summary>
    private async Task StoreCoreAsync(InvokedContext context, CancellationToken cancellationToken)
    {
        var scope = new MemoryScope
        {
            TenantId = permit.TenantId,
            UserId = permit.UserId,
            ChannelId = permit.ChannelId
        };

        var assistantText = string.Join("\n", context.ResponseMessages?.Select(static m => m.Text) ?? []);
        var userText = context.RequestMessages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;

        if (string.IsNullOrWhiteSpace(assistantText) && string.IsNullOrWhiteSpace(userText))
        {
            return;
        }

        var sessionId = permit.SessionId;
        string? stateSessionId = null;
        context.Session?.StateBag.TryGetValue<string>(DbChatHistoryProvider.ChatSessionIdKey, out stateSessionId);
        var turnId = Guid.NewGuid().ToString("N");
        if (!string.IsNullOrWhiteSpace(stateSessionId))
        {
            sessionId ??= stateSessionId;
            turnId = stateSessionId;
        }
        var recordedAt = timeProvider.GetUtcNow();

        try
        {
            var recentHistory = context.RequestMessages
                .Where(static m => m.Role == ChatRole.User || m.Role == ChatRole.Assistant)
                .TakeLast(8)
                .ToList();

            var facts = await factExtractionService.ExtractFactsAsync(
                userText,
                assistantText,
                recentHistory,
                cancellationToken).ConfigureAwait(false);

            if (facts.Count == 0)
            {
                return;
            }

            foreach (var fact in facts)
            {
                var seed = factExtractionService.RenderSeedPage(fact, sessionId, turnId, recordedAt);
                var seedSnapshot = parser.Parse(string.Empty, seed);

                var related = await memoryClient.SearchMemoriesAsync(scope, fact, 24, cancellationToken).ConfigureAwait(false);
                var relatedPages = related
                    .Select(item => parser.Parse(item.Key, item.Text))
                    .ToList();

                var result = await normalizer.NormalizeAsync(seedSnapshot, relatedPages, enableRepair: true, cancellationToken)
                    .ConfigureAwait(false);
                await memoryClient.SaveMemoryAsync(scope, result.ScopeRelativeKey, result.Content, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Memory normalization failed; degrading to raw-text save.");
            var rawContent = !string.IsNullOrWhiteSpace(assistantText) ? assistantText : userText ?? string.Empty;
            var fallbackFact = renderer.RenderSeedPage(rawContent, sessionId, turnId, recordedAt);
            var fallbackKey = $"facts/what/fact-fallback/{Guid.NewGuid():N}";
            await memoryClient.SaveMemoryAsync(scope, fallbackKey, fallbackFact, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds a compact one-line summary for a stored memory page.
    /// </summary>
    private string BuildCompactSummary(string key, string content)
    {
        try
        {
            var page = parser.Parse(key, content);
            var dims = string.Join(",", new[] { page.PrimaryDimension }.Concat(page.SecondaryDimensions).Distinct(StringComparer.Ordinal));
            var summary = $"- {page.PrimaryDimension}: {page.FactText} [dimensions: {dims}] [links: {page.Links.Count}]";
            return summary.Length <= 200 ? summary : summary[..200];
        }
        catch
        {
            return content.Length <= 200 ? content : content[..200];
        }
    }
}
