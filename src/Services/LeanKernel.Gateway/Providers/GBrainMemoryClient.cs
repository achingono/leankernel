using System.Text.Json;
using System.Text.Json.Serialization;
using LeanKernel.Logic.Providers;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Gateway.Providers;

/// <summary>
/// Implements <see cref="IMemoryClient"/> backed by the GBrain MCP service.
/// Maps memory operations to GBrain MCP tool calls: search and put_page.
/// </summary>
public sealed class GBrainMemoryClient : IMemoryClient
{
    private readonly IGBrainMcpClient _client;
    private readonly ILogger<GBrainMemoryClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GBrainMemoryClient"/> class.
    /// </summary>
    /// <param name="client">The low-level MCP client used to call GBrain tools.</param>
    /// <param name="logger">The logger for memory operation diagnostics.</param>
    public GBrainMemoryClient(IGBrainMcpClient client, ILogger<GBrainMemoryClient> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(
        MemoryScope scope,
        string query,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        _logger.LogDebug("GBrain memory search: {Query} (max={Max}, tenant={Tenant})",
            query, maxResults, scope.TenantId);

        try
        {
            var result = await _client.CallToolAsync("search", new
            {
                query,
                limit = maxResults,
                namespace_name = scope.Namespace
            }, ct).ConfigureAwait(false);

            if (result is null)
            {
                return [];
            }

            return DeserializeSearchResults(result.Value);
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
        _logger.LogDebug("GBrain memory save: {Key} ({Length} chars, tenant={Tenant})",
            key, content.Length, scope.TenantId);

        var slug = BuildScopedSlug(scope, key);

        await _client.CallToolAsync("put_page", new
        {
            slug,
            content
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the GBrain page slug used to persist a scoped memory item.
    /// </summary>
    /// <param name="scope">The memory scope being persisted.</param>
    /// <param name="key">The caller-provided memory key.</param>
    /// <returns>The scoped GBrain slug.</returns>
    private static string BuildScopedSlug(MemoryScope scope, string key)
    {
        return $"memory/{scope.TenantId}/{scope.UserId}/{scope.ChannelId}/{key}";
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
    private static MemoryItem MapToMemoryItem(GBrainMemorySearchItem item) => new()
    {
        Key = item.Key,
        Text = item.Content,
        Score = item.Score,
        Source = "gbrain"
    };
}

/// <summary>
/// Represents the top-level payload returned by GBrain memory search responses.
/// </summary>
internal sealed class GBrainMemorySearchResult
{
    [JsonPropertyName("results")]
    public List<GBrainMemorySearchItem>? Results { get; set; }
}

/// <summary>
/// Represents a single memory search result item returned by GBrain.
/// </summary>
internal sealed class GBrainMemorySearchItem
{
    [JsonPropertyName("slug")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("compiled_truth")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }
}
