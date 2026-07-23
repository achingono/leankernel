namespace LeanKernel.Gateway.Memory;

using System.Text.Json;

using LeanKernel;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Logic.Providers;

using Microsoft.Extensions.Options;

/// <summary>
/// GBrain-backed implementation of <see cref="IDocumentStoreClient"/>.
/// Uses GBrain's page storage and search tools for document catalog persistence.
/// </summary>
public sealed class GBrainDocumentStoreClient : IDocumentStoreClient
{
    private readonly IGBrainMcpClient _client;
    private readonly ILogger<GBrainDocumentStoreClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GBrainDocumentStoreClient"/> class.
    /// </summary>
    /// <param name="client">The GBrain MCP client.</param>
    /// <param name="settings">The GBrain settings.</param>
    /// <param name="logger">The logger instance.</param>
    public GBrainDocumentStoreClient(
        IGBrainMcpClient client,
        IOptions<GBrainSettings> settings,
        ILogger<GBrainDocumentStoreClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(DocumentScopeContext scope, string fingerprint, CancellationToken ct = default)
    {
        var slug = BuildSlug(scope, fingerprint);
        try
        {
            var result = await _client.CallToolAsync("get_page", new { slug }, ct);
            return result is not null;
        }
        catch (GBrainException ex) when (ex.ErrorCode == -32601)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(DocumentScopeContext scope, string fingerprint, DocumentCatalogEntry document, CancellationToken ct = default)
    {
        var slug = BuildSlug(scope, fingerprint);
        var content = JsonSerializer.Serialize(document, Constants.Serialization.JsonOptions);

        await _client.CallToolAsync("put_page", new { slug, content }, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentSearchHit>> SearchAsync(
        DocumentScopeContext scope,
        string query,
        IReadOnlyList<Guid>? channelIds,
        int maxResults,
        CancellationToken ct = default)
    {
        try
        {
            var namespacePrefix = BuildNamespacePrefix(scope);
            var result = await _client.CallToolAsync("search", new { query, limit = maxResults, ns = namespacePrefix }, ct);

            if (result is null)
            {
                return [];
            }

            var results = DeserializeSearchResults(result.Value);
            return FilterByChannelIds(results, channelIds);
        }
        catch (GBrainException ex)
        {
            _logger.LogWarning(ex, "Document search failed for query: {Query}", query);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentCatalogEntry>> ListAsync(
        DocumentScopeContext scope,
        IReadOnlyList<Guid>? channelIds,
        int limit,
        CancellationToken ct = default)
    {
        try
        {
            var namespacePrefix = BuildNamespacePrefix(scope);
            var result = await _client.CallToolAsync("search", new { query = string.Empty, limit, ns = namespacePrefix }, ct);

            if (result is null)
            {
                return [];
            }

            var results = DeserializeListResults(result.Value);
            return FilterCatalogByChannelIds(results, channelIds);
        }
        catch (GBrainException ex)
        {
            _logger.LogWarning(ex, "Document list failed.");
            return [];
        }
    }

    private static string BuildSlug(DocumentScopeContext scope, string fingerprint)
    {
        var ns = BuildNamespacePrefix(scope);
        return $"{ns}/{fingerprint}";
    }

    private static string BuildNamespacePrefix(DocumentScopeContext scope)
    {
        var scopeStr = scope.AvailabilityScope.ToString().ToLowerInvariant();
        return $"documents/{scope.TenantId}/{scopeStr}/{scope.ChannelId}/{scope.UserId}";
    }

    private static IReadOnlyList<DocumentSearchHit> DeserializeSearchResults(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Array)
        {
            return result.EnumerateArray().Select(MapToSearchHit).ToList();
        }

        if (result.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            return results.EnumerateArray().Select(MapToSearchHit).ToList();
        }

        return [];
    }

    private static IReadOnlyList<DocumentCatalogEntry> DeserializeListResults(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Array)
        {
            return result.EnumerateArray().Select(MapToCatalogEntry).ToList();
        }

        if (result.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            return results.EnumerateArray().Select(MapToCatalogEntry).ToList();
        }

        return [];
    }

    private static DocumentSearchHit MapToSearchHit(JsonElement item)
    {
        var slug = item.TryGetProperty("slug", out var s) ? s.GetString() ?? string.Empty : string.Empty;
        var fingerprint = slug;
        var content = ExtractContent(item, ["compiled_truth", "content", "chunk_text"]);

        var score = item.TryGetProperty("score", out var sc) && sc.TryGetDouble(out var d) ? d : 0.0;
        var fileName = item.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;

        return new DocumentSearchHit(
            Fingerprint: fingerprint,
            FileName: fileName,
            ContentType: "application/octet-stream",
            Excerpt: Truncate(content, 200),
            Score: score,
            IngestedAt: DateTime.UtcNow);
    }

    private static DocumentCatalogEntry MapToCatalogEntry(JsonElement item)
    {
        var slug = item.TryGetProperty("slug", out var s) ? s.GetString() ?? string.Empty : string.Empty;
        var content = ExtractContent(item, ["compiled_truth", "content"]);

        var (channelId, userId) = ParseIdentityFromSlug(slug);

        return new DocumentCatalogEntry(
            slug,
            string.Empty,
            "application/octet-stream",
            content ?? string.Empty,
            Guid.Empty, userId, Guid.Empty, channelId,
            DocumentAvailabilityScope.Channel,
            DateTime.UtcNow);
    }

    private static (Guid ChannelId, Guid UserId) ParseIdentityFromSlug(string slug)
    {
        var parts = slug.Split('/');
        if (parts.Length >= 5 && Guid.TryParse(parts[3], out var cid) && Guid.TryParse(parts[4], out var uid))
        {
            return (cid, uid);
        }

        return (Guid.Empty, Guid.Empty);
    }

    private static IReadOnlyList<DocumentSearchHit> FilterByChannelIds(
        IReadOnlyList<DocumentSearchHit> results,
        IReadOnlyList<Guid>? channelIds)
    {
        if (channelIds == null || channelIds.Count == 0)
        {
            return results;
        }

        return results.Where(r =>
        {
            var parts = r.Fingerprint.Split('/');
            return parts.Length >= 4 && Guid.TryParse(parts[3], out var cid) && channelIds.Contains(cid);
        }).ToList();
    }

    private static IReadOnlyList<DocumentCatalogEntry> FilterCatalogByChannelIds(
        IReadOnlyList<DocumentCatalogEntry> results,
        IReadOnlyList<Guid>? channelIds)
    {
        if (channelIds == null || channelIds.Count == 0)
        {
            return results;
        }

        return results.Where(r => channelIds.Contains(r.ChannelId)).ToList();
    }

    private static string ExtractContent(JsonElement item, string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (item.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                {
                    var text = prop.GetString();
                    if (text != null)
                    {
                        return text;
                    }
                }

                if (prop.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    return prop.ToString();
                }
            }
        }

        return string.Empty;
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
