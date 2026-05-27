using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Knowledge;

/// <summary>
/// Provides LeanKernel knowledge operations by calling the garrytan/gbrain MCP tools.
/// </summary>
public sealed class GBrainKnowledgeService(GBrainMcpClient client, ILogger<GBrainKnowledgeService> logger) : IKnowledgeService
{
    private readonly GBrainMcpClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly ILogger<GBrainKnowledgeService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Knowledge search: {Query} (max={Max})", query, maxResults);

        var result = await _client.CallToolAsync("search", new { query, limit = maxResults }, ct);
        if (result is null)
        {
            return [];
        }

        var searchResults = DeserializeSearchResults(result.Value);
        if (searchResults.Count == 0)
        {
            return [];
        }

        return searchResults
            .Select(r => new RetrievalCandidate
            {
                Key = r.Key,
                Content = r.Content,
                Source = "gbrain",
                Score = r.Score,
                TokenCount = EstimateTokens(r.Content),
                Metadata = CreateMetadata(r)
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
    {
        _logger.LogDebug("Knowledge get_page: {Key}", key);

        try
        {
            var result = await _client.CallToolAsync("get_page", new { slug = key }, ct);
            if (result is null)
            {
                return null;
            }

            var page = result.Value.Deserialize<GBrainPageResult>();
            if (page is null)
            {
                return null;
            }

            return new KnowledgePage
            {
                Key = page.Key,
                Content = page.Content,
                LastModified = page.LastModified,
                LinkedPages = page.Links?
                    .Select(link => link.ToSlug)
                    .Where(static linkedPage => !string.IsNullOrWhiteSpace(linkedPage))
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            };
        }
        catch (GBrainException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task PutPageAsync(string key, string content, CancellationToken ct = default)
    {
        _logger.LogDebug("Knowledge put_page: {Key} ({Length} chars)", key, content.Length);
        await _client.CallToolAsync("put_page", new { slug = key, content }, ct);
    }

    /// <inheritdoc />
    public async Task DeletePageAsync(string key, CancellationToken ct = default)
    {
        _logger.LogDebug("Knowledge delete_page: {Key}", key);
        try
        {
            await _client.CallToolAsync("delete_page", new { slug = key }, ct);
        }
        catch (GBrainException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            // Page already doesn't exist; treat as success for idempotency
            _logger.LogDebug("Delete failed for non-existent page {Key}", key);
        }
    }

    private static IReadOnlyList<GBrainSearchItem> DeserializeSearchResults(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Array)
        {
            return result.Deserialize<List<GBrainSearchItem>>() ?? [];
        }

        var searchResults = result.Deserialize<GBrainSearchResult>();
        return searchResults?.Results ?? [];
    }

    private static Dictionary<string, string>? CreateMetadata(GBrainSearchItem item)
    {
        Dictionary<string, string>? metadata = item.Metadata is null
            ? null
            : new Dictionary<string, string>(item.Metadata);

        if (item.PageId is not null)
        {
            metadata ??= [];
            metadata["page_id"] = item.PageId.Value.ToString(CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    private static int EstimateTokens(string text) => text.Length / 4;
}

internal sealed class GBrainSearchResult
{
    [JsonPropertyName("results")]
    public List<GBrainSearchItem>? Results { get; set; }
}

internal sealed class GBrainSearchItem
{
    [JsonPropertyName("slug")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("compiled_truth")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("page_id")]
    public int? PageId { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

internal sealed class GBrainPageResult
{
    [JsonPropertyName("slug")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("compiled_truth")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? LastModified { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("links")]
    public List<GBrainLink>? Links { get; set; }
}

internal sealed class GBrainLink
{
    [JsonPropertyName("to_slug")]
    public string ToSlug { get; set; } = string.Empty;

    [JsonPropertyName("link_type")]
    public string? LinkType { get; set; }
}
