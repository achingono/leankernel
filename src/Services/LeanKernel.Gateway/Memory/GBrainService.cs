using System.Text.Json;
using System.Text.Json.Serialization;

using LeanKernel.Logic.Memory;

namespace LeanKernel.Gateway.Memory;

/// <summary>
/// Implements <see cref="IMemoryService"/> backed by the GBrain MCP service.
/// Maps memory operations to GBrain MCP tool calls: search, get_page, put_page.
/// </summary>
public sealed class GBrainService : IMemoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IGBrainMcpClient _client;
    private readonly ILogger<GBrainService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GBrainService"/>.
    /// </summary>
    public GBrainService(IGBrainMcpClient client, ILogger<GBrainService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        _logger.LogDebug("GBrain knowledge search: {Query} (max={Max})", query, maxResults);

        try
        {
            var result = await _client.CallToolAsync("search", new { query, limit = maxResults }, ct)
                .ConfigureAwait(false);

            if (result is null)
            {
                return [];
            }

            return DeserializeSearchResults(result.Value);
        }
        catch (GBrainException ex)
        {
            _logger.LogWarning(ex, "GBrain knowledge search failed for query: {Query}", query);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<MemoryPage?> GetPageAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _logger.LogDebug("GBrain knowledge get_page: {Key}", key);

        try
        {
            var result = await _client.CallToolAsync("get_page", new { slug = key }, ct)
                .ConfigureAwait(false);

            if (result is null)
            {
                return null;
            }

            return DeserializePage(key, result.Value);
        }
        catch (GBrainException ex)
        {
            _logger.LogWarning(ex, "GBrain get_page failed for key: {Key}", key);
            return null;
        }
    }

    /// <inheritdoc />
    public Task PutPageAsync(string key, string content, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);
        return PutPageCoreAsync(key, content, ct);
    }

    private async Task PutPageCoreAsync(string key, string content, CancellationToken ct)
    {
        _logger.LogDebug("GBrain knowledge put_page: {Key} ({Length} chars)", key, content.Length);

        await _client.CallToolAsync("put_page", new { slug = key, content }, ct)
            .ConfigureAwait(false);
    }

    private static IReadOnlyList<MemorySearchResult> DeserializeSearchResults(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Array)
        {
            return result.EnumerateArray()
                .Select(MapSearchItem)
                .ToList();
        }

        if (result.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            return results.EnumerateArray()
                .Select(MapSearchItem)
                .ToList();
        }

        return [];
    }

    private static MemorySearchResult MapSearchItem(JsonElement item)
    {
        var key = item.TryGetProperty("slug", out var s) ? s.GetString() ?? string.Empty : string.Empty;
        var content = string.Empty;
        if (item.TryGetProperty("compiled_truth", out var compiledTruth) && !string.IsNullOrWhiteSpace(compiledTruth.GetString()))
        {
            content = compiledTruth.GetString() ?? string.Empty;
        }
        else if (item.TryGetProperty("chunk_text", out var chunkText) && !string.IsNullOrWhiteSpace(chunkText.GetString()))
        {
            content = chunkText.GetString() ?? string.Empty;
        }
        else if (item.TryGetProperty("content", out var rawContent) && !string.IsNullOrWhiteSpace(rawContent.GetString()))
        {
            content = rawContent.GetString() ?? string.Empty;
        }
        else if (item.TryGetProperty("title", out var title) && !string.IsNullOrWhiteSpace(title.GetString()))
        {
            content = title.GetString() ?? string.Empty;
        }

        var score = item.TryGetProperty("score", out var sc) && sc.TryGetDouble(out var d) ? d : 0.0;

        return new MemorySearchResult { Key = key, Content = content, Score = score };
    }

    private static MemoryPage? DeserializePage(string requestedKey, JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var page = result.Deserialize<GBrainPageDto>(JsonOptions);
        if (page is null)
        {
            return null;
        }

        return new MemoryPage
        {
            Key = page.Slug ?? requestedKey,
            Content = page.CompiledTruth ?? page.Content ?? string.Empty,
            LastModified = page.UpdatedAt
        };
    }

    private sealed class GBrainPageDto
    {
        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("compiled_truth")]
        public string? CompiledTruth { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}