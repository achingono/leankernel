using LeanKernel.Entities;
using LeanKernel.Logic.Memory;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Logic.Providers;

/// <summary>
/// Provides AI context (memory retrieval) backed by Memory via <see cref="IMemoryClient"/>,
/// scoped by tenant/person/channel identity.
/// </summary>
public class MemoryProvider(
    IMemoryClient memoryClient,
    IPermit permit,
    IChannelMemoryPolicyResolver memoryPolicyResolver,
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
        {
            return new AIContext { Messages = [] };
        }

        var scope = new MemoryScope
        {
            TenantId = permit.TenantId,
            PersonId = permit.PersonId,
            ChannelId = permit.ChannelId
        };

        try
        {
            var memories = await memoryClient.SearchMemoriesAsync(
                scope, queryText, MaxMemoryResults, cancellationToken);

            if (memories.Count == 0)
            {
                return new AIContext { Messages = [] };
            }

            var admitted = ApplyOverlayPrecedence(memories, scope.ChannelId)
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
            logger.LogWarning(ex, "Memory search failed for scope (tenant={TenantId}, person={PersonId}, channel={ChannelId}); continuing without memory context.",
                scope.TenantId, scope.PersonId, scope.ChannelId);
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
            PersonId = permit.PersonId,
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

                var policy = await memoryPolicyResolver
                    .ResolveAsync(scope.TenantId, scope.ChannelId, cancellationToken)
                    .ConfigureAwait(false);
                var relatedScope = new MemoryScope
                {
                    TenantId = scope.TenantId,
                    PersonId = scope.PersonId,
                    ChannelId = scope.ChannelId,
                    SearchChannelIds = policy.MutuallyVisibleChannelIds
                };

                var related = await memoryClient.SearchMemoriesAsync(relatedScope, fact, 24, cancellationToken).ConfigureAwait(false);
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

            try
            {
                await memoryClient.SaveMemoryAsync(scope, fallbackKey, fallbackFact, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception fallbackEx)
            {
                logger.LogWarning(
                    fallbackEx,
                    "Fallback memory save failed for scope (tenant={TenantId}, person={PersonId}, channel={ChannelId}); continuing turn without persistence.",
                    scope.TenantId,
                    scope.PersonId,
                    scope.ChannelId);
            }
        }
    }

    private IReadOnlyList<MemoryItem> ApplyOverlayPrecedence(IReadOnlyList<MemoryItem> memories, Guid localChannelId)
    {
        if (memories.Count <= 1)
        {
            return memories;
        }

        var resolved = new Dictionary<string, (MemoryItem Item, MemoryPageSnapshot Snapshot)>(StringComparer.Ordinal);

        foreach (var memory in memories)
        {
            MemoryPageSnapshot snapshot;

            try
            {
                snapshot = parser.Parse(memory.Key, memory.Text);
            }
            catch
            {
                resolved.TryAdd(memory.Key, (memory, new MemoryPageSnapshot(
                    memory.Key,
                    memory.Text,
                    memory.Text,
                    memory.Text,
                    DateTimeOffset.MinValue,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string?>(),
                    null,
                    null,
                    [],
                    null,
                    "what",
                    [],
                    [],
                    false)));
                continue;
            }

            var groupKey = BuildOverlayGroupKey(memory, snapshot);

            if (!resolved.TryGetValue(groupKey, out var existing))
            {
                resolved[groupKey] = (memory, snapshot);
                continue;
            }

            if (CompareOverlay(memory, snapshot, existing.Item, existing.Snapshot, localChannelId) > 0)
            {
                resolved[groupKey] = (memory, snapshot);
            }
        }

        return resolved.Values
            .Select(entry => entry.Item)
            .ToList();
    }

    private static int CompareOverlay(
        MemoryItem candidate,
        MemoryPageSnapshot candidateSnapshot,
        MemoryItem existing,
        MemoryPageSnapshot existingSnapshot,
        Guid localChannelId)
    {
        var candidateLocal = candidate.ChannelId == localChannelId;
        var existingLocal = existing.ChannelId == localChannelId;

        if (candidateLocal != existingLocal)
        {
            return candidateLocal ? 1 : -1;
        }

        var timeComparison = candidateSnapshot.EffectiveTimestamp.CompareTo(existingSnapshot.EffectiveTimestamp);
        if (timeComparison != 0)
        {
            return timeComparison;
        }

        return candidate.Score.CompareTo(existing.Score);
    }

    private static string BuildOverlayGroupKey(MemoryItem item, MemoryPageSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(item.ScopeRelativeKey))
        {
            var parts = item.ScopeRelativeKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && string.Equals(parts[0], "facts", StringComparison.OrdinalIgnoreCase))
            {
                return $"facts/{parts[1]}/{parts[2]}";
            }
        }

        return $"facts/{MemoryPageFields.NormalizeDimension(snapshot.PrimaryDimension)}/{snapshot.NormalizedFactText}";
    }

    /// <summary>
    /// Builds a compact one-line summary for a stored memory page.
    /// </summary>
    private string BuildCompactSummary(string key, string content)
    {
        static string Truncate(string text)
            => text.Length <= 200 ? text : text[..200];

        try
        {
            var page = parser.Parse(key, content);

            if (string.IsNullOrWhiteSpace(page.FactText))
            {
                return Truncate(content);
            }

            var dims = string.Join(",", new[] { page.PrimaryDimension }.Concat(page.SecondaryDimensions).Distinct(StringComparer.Ordinal));
            var summary = $"- {page.PrimaryDimension}: {page.FactText} [dimensions: {dims}] [links: {page.Links.Count}]";
            return Truncate(summary);
        }
        catch (FormatException ex)
        {
            logger.LogDebug(ex, "Memory page parse failed for key {Key}; using raw content fallback.", key);
            return Truncate(content);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "Memory page render failed for key {Key}; using raw content fallback.", key);
            return Truncate(content);
        }
    }
}