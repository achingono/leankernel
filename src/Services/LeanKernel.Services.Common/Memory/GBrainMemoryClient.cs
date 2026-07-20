using System.Text.Json;
using System.Text.Json.Serialization;

using LeanKernel.Entities;
using LeanKernel.Logic.Providers;

using Microsoft.Extensions.Logging;

namespace LeanKernel.Gateway.Memory;

/// <summary>
/// Implements <see cref="IMemoryClient"/> backed by the GBrain MCP service.
/// </summary>
public sealed class GBrainMemoryClient : IMemoryClient
{
    private readonly IGBrainMcpClient _client;
    private readonly IChannelMemoryPolicyResolver _memoryPolicyResolver;
    private readonly ILogger<GBrainMemoryClient> _logger;

    public GBrainMemoryClient(
        IGBrainMcpClient client,
        IChannelMemoryPolicyResolver memoryPolicyResolver,
        ILogger<GBrainMemoryClient> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _memoryPolicyResolver = memoryPolicyResolver ?? throw new ArgumentNullException(nameof(memoryPolicyResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(
        MemoryScope scope,
        string query,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        _logger.LogDebug("GBrain memory search: {Query} (max={Max}, tenant={Tenant}, person={Person})",
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
                var result = await _client.CallToolAsync(Constants.GBrain.SearchTool, new
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

    public async Task SaveMemoryAsync(
        MemoryScope scope,
        string key,
        string content,
        CancellationToken ct = default)
    {
        _logger.LogDebug("GBrain memory save: {Key} ({Length} chars, tenant={Tenant})",
            key, content.Length, scope.TenantId);

        var slug = BuildScopedSlug(scope, key);

        await _client.CallToolAsync(Constants.GBrain.PutPageTool, new
        {
            slug,
            content
        }, ct).ConfigureAwait(false);
    }

    private static string BuildScopedNamespace(MemoryScope scope, Guid channelId)
    {
        return $"{Constants.GBrain.MemoryPrefix}/{scope.TenantId}/{scope.PersonId}/{channelId}";
    }

    private static string BuildScopedSlug(MemoryScope scope, string key)
    {
        return $"{Constants.GBrain.MemoryPrefix}/{scope.TenantId}/{scope.PersonId}/{scope.ChannelId}/{key}";
    }

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

    private static MemoryItem MapToMemoryItem(GBrainMemorySearchItem item)
    {
        var (channelId, scopeRelativeKey) = TryParseScopedKey(item.Key);

        return new MemoryItem
        {
            Key = item.Key,
            Text = item.GetBestContent(),
            Score = item.Score,
            Source = Constants.GBrain.Source,
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
        if (parts.Length < 5 || !string.Equals(parts[0], Constants.GBrain.MemoryPrefix, StringComparison.OrdinalIgnoreCase))
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

internal sealed class GBrainMemorySearchResult
{
    [JsonPropertyName(Constants.GBrain.Results)]
    public List<GBrainMemorySearchItem>? Results { get; set; }
}

internal sealed class GBrainMemorySearchItem
{
    [JsonPropertyName(Constants.GBrain.Slug)]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName(Constants.GBrain.CompiledTruth)]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName(Constants.GBrain.ChunkText)]
    public string ChunkText { get; set; } = string.Empty;

    [JsonPropertyName(Constants.GBrain.Content)]
    public string RawContent { get; set; } = string.Empty;

    [JsonPropertyName(Constants.GBrain.Title)]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName(Constants.GBrain.Score)]
    public double Score { get; set; }

    public string GetBestContent()
    {
        if (!string.IsNullOrWhiteSpace(Content))
        {
            return Content;
        }

        if (!string.IsNullOrWhiteSpace(ChunkText))
        {
            return ChunkText;
        }

        if (!string.IsNullOrWhiteSpace(RawContent))
        {
            return RawContent;
        }

        return Title;
    }
}
    /// <summary>
    /// Initializes a new instance of the <see cref="GBrainMemoryClient"/> class.
    /// </summary>
    /// <inheritdoc />
    /// <inheritdoc />
