using System.Text.Json;

using LeanKernel.Entities;
using LeanKernel.Logic.Providers;

namespace LeanKernel.Gateway.Memory;

/// <summary>
/// Implements <see cref="IMemoryClient"/> backed by the GBrain MCP service.
/// Maps memory operations to GBrain MCP tool calls: search and put_page.
/// </summary>
public sealed class GBrainMemoryClient : IMemoryClient
{
    private readonly IGBrainMcpClient _client;
    private readonly IChannelMemoryPolicyResolver _memoryPolicyResolver;
    private readonly ILogger<GBrainMemoryClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GBrainMemoryClient"/> class.
    /// </summary>
    /// <param name="client">The low-level MCP client used to call GBrain tools.</param>
    /// <param name="memoryPolicyResolver">The channel memory policy resolver.</param>
    /// <param name="logger">The logger for memory operation diagnostics.</param>
    public GBrainMemoryClient(
        IGBrainMcpClient client,
        IChannelMemoryPolicyResolver memoryPolicyResolver,
        ILogger<GBrainMemoryClient> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _memoryPolicyResolver = memoryPolicyResolver ?? throw new ArgumentNullException(nameof(memoryPolicyResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(
        MemoryScope scope,
        string query,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "GBrain memory search: {Query} (max={Max}, tenant={Tenant}, person={Person})",
            query, maxResults, scope.TenantId, scope.PersonId);

        try
        {
            var channelIds = scope.SearchChannelIds?.Count > 0
                ? scope.SearchChannelIds.Distinct().ToArray()
                : (await _memoryPolicyResolver.ResolveAsync(scope.TenantId, scope.ChannelId, ct).ConfigureAwait(false))
                    .ReadableChannelIds
                    .Append(scope.ChannelId)
                    .Distinct()
                    .ToArray();

            var aggregated = new Dictionary<string, MemoryItem>(StringComparer.Ordinal);

            foreach (var channelId in channelIds)
            {
                var result = await _client.CallToolAsync("search", new
                {
                    query,
                    limit = maxResults,
                    namespace_name = BuildScopedNamespace(scope, channelId)
                }, ct).ConfigureAwait(false);

                if (result is null)
                {
                    continue;
                }

                foreach (var item in DeserializeSearchResults(result.Value))
                {
                    if (!aggregated.TryGetValue(item.Key, out var existing) || item.Score > existing.Score)
                    {
                        aggregated[item.Key] = item;
                    }
                }
            }

            return aggregated.Values
                .OrderByDescending(item => item.Score)
                .Take(maxResults)
                .ToList();
        }
        catch (GBrainException ex)
        {
            _logger.LogWarning(ex, "GBrain memory search failed for query: {Query}", query);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task SaveMemoryAsync(
        MemoryScope scope,
        string key,
        string content,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "GBrain memory save: {Key} ({Length} chars, tenant={Tenant})",
            key, content.Length, scope.TenantId);

        var slug = BuildScopedSlug(scope, key);

        await _client.CallToolAsync("put_page", new
        {
            slug,
            content
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the GBrain namespace used to scope memory search results.
    /// Derived from the same tenant/person/channel identifiers used by <see cref="BuildScopedSlug"/>.
    /// </summary>
    /// <param name="scope">The memory scope to derive the namespace from.</param>
    /// <param name="channelId">The channel identifier to include.</param>
    /// <returns>The scoped namespace string.</returns>
    private static string BuildScopedNamespace(MemoryScope scope, Guid channelId)
    {
        return $"memory/{scope.TenantId}/{scope.PersonId}/{channelId}";
    }

    /// <summary>
    /// Builds the GBrain page slug used to persist a scoped memory item.
    /// </summary>
    /// <param name="scope">The memory scope being persisted.</param>
    /// <param name="key">The caller-provided memory key.</param>
    /// <returns>The scoped GBrain slug.</returns>
    private static string BuildScopedSlug(MemoryScope scope, string key)
    {
        return $"memory/{scope.TenantId}/{scope.PersonId}/{scope.ChannelId}/{key}";
    }

    /// <summary>
    /// Deserializes GBrain search results into LeanKernel memory items.
    /// </summary>
    /// <param name="result">The raw JSON payload returned by the search tool.</param>
    /// <returns>The mapped memory items.</returns>
    private static IReadOnlyList<MemoryItem> DeserializeSearchResults(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Array)
        {
            var items = result.Deserialize<List<GBrainMemorySearchItem>>();
            return items?.Select(MapToMemoryItem).ToList() ?? [];
        }

        var searchResult = result.Deserialize<GBrainMemorySearchResult>();
        return searchResult?.Results?.Select(MapToMemoryItem).ToList() ?? [];
    }

    /// <summary>
    /// Maps a GBrain search item into a LeanKernel <see cref="MemoryItem"/>.
    /// </summary>
    /// <param name="item">The GBrain search item to map.</param>
    /// <returns>The mapped memory item.</returns>
    private static MemoryItem MapToMemoryItem(GBrainMemorySearchItem item)
    {
        var (channelId, scopeRelativeKey) = TryParseScopedKey(item.Key);

        return new MemoryItem
        {
            Key = item.Key,
            Text = item.GetBestContent(),
            Score = item.Score,
            Source = "gbrain",
            ChannelId = channelId,
            ScopeRelativeKey = scopeRelativeKey
        };
    }

    private static (Guid? ChannelId, string? ScopeRelativeKey) TryParseScopedKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return (null, null);
        }

        var parts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5 || !string.Equals(parts[0], "memory", StringComparison.OrdinalIgnoreCase))
        {
            return (null, key);
        }

        var scopeRelativeKey = string.Join('/', parts.Skip(4));
        if (!Guid.TryParse(parts[3], out var channelId))
        {
            return (null, scopeRelativeKey);
        }

        return (channelId, scopeRelativeKey);
    }
}