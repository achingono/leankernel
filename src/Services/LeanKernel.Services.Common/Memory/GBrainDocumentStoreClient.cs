namespace LeanKernel.Services.Common.Memory;

using System.Globalization;
using System.Text.Json;

using LeanKernel;
using LeanKernel.Logic.Providers;
using LeanKernel.Services.Common.Configuration;
using LeanKernel.Services.Common.Interfaces;

using Microsoft.Extensions.Logging;
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
        this._client = client;
        this._logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(DocumentScopeContext scope, string fingerprint, CancellationToken ct = default)
    {
        var slug = BuildSlug(scope, fingerprint);
        try
        {
            var result = await this._client.CallToolAsync("get_page", new { slug }, ct);
            return result is not null;
        }
        catch (GBrainException ex) when (ex.ErrorCode == -32601 || ex.Message.Contains("page_not_found", StringComparison.Ordinal))
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(DocumentScopeContext scope, string fingerprint, DocumentCatalogEntry document, CancellationToken ct = default)
    {
        var slug = BuildSlug(scope, fingerprint);
        var content = JsonSerializer.Serialize(document, Constants.Serialization.JsonOptions);

        await this._client.CallToolAsync("put_page", new { slug, content }, ct);
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
            var result = await this._client.CallToolAsync("search", new { query, limit = maxResults * 3, ns = BuildNamespacePrefix(scope) }, ct);

            if (result is null)
            {
                return [];
            }

            var results = DeserializeSearchResults(result.Value)
                .Where(hit => IsReadable(scope, hit.Fingerprint, channelIds))
                .ToList();

            this._logger.LogDebug("Document search candidate set: {Candidates} hits before merge.", results.Count);

            return results
                .GroupBy(hit => GetFingerprintFromSlug(hit.Fingerprint), StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(hit => hit.Score)
                    .ThenBy(hit => hit.Fingerprint, StringComparer.Ordinal)
                    .First())
                .OrderByDescending(hit => hit.Score)
                .ThenBy(hit => hit.Fingerprint, StringComparer.Ordinal)
                .Take(maxResults)
                .ToList();
        }
        catch (GBrainException ex)
        {
            this._logger.LogWarning(ex, "Document search failed for query: {Query}", query);
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
            const int RemotePageLimit = 100;
            var allResults = new List<DocumentCatalogEntry>();
            var pagesToFetch = (int)Math.Ceiling((limit * 3.0) / RemotePageLimit);

            for (int page = 0; page < pagesToFetch; page++)
            {
                var offset = page * RemotePageLimit;
                var result = await this._client.CallToolAsync("list_pages", new { type = "document", sort = "updated_desc", limit = RemotePageLimit, offset }, ct);

                if (result is null)
                {
                    break;
                }

                var pageResults = DeserializeListResults(result.Value)
                    .Where(entry => IsReadable(scope, entry.Fingerprint, channelIds))
                    .ToList();

                allResults.AddRange(pageResults);

                if (pageResults.Count < RemotePageLimit)
                {
                    break;
                }
            }

            this._logger.LogDebug("Document list candidate set: {Candidates} pages before merge.", allResults.Count);

            return allResults
                .GroupBy(entry => GetFingerprintFromSlug(entry.Fingerprint), StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(entry => entry.IngestedAt)
                    .ThenBy(entry => entry.Fingerprint, StringComparer.Ordinal)
                    .First())
                .OrderByDescending(entry => entry.IngestedAt)
                .ThenBy(entry => entry.Fingerprint, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
        }
        catch (GBrainException ex)
        {
            this._logger.LogWarning(ex, "Document list failed.");
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
            ContentType: Constants.ContentTypes.ApplicationOctetStream,
            Excerpt: Truncate(content, 200),
            Score: score,
            IngestedAt: DateTime.UtcNow);
    }

    private static DocumentCatalogEntry MapToCatalogEntry(JsonElement item)
    {
        var slug = item.TryGetProperty("slug", out var s) ? s.GetString() ?? string.Empty : string.Empty;
        var content = ExtractContent(item, ["compiled_truth", "content"]);
        var parts = slug.Split('/');

        var tenantId = parts.Length > 1 && Guid.TryParse(parts[1], out var t) ? t : Guid.Empty;
        var channelId = parts.Length > 3 && Guid.TryParse(parts[3], out var c) ? c : Guid.Empty;
        var userId = parts.Length > 4 && Guid.TryParse(parts[4], out var u) ? u : Guid.Empty;

        return new DocumentCatalogEntry(
            slug,
            string.Empty,
            Constants.ContentTypes.ApplicationOctetStream,
            content ?? string.Empty,
            tenantId, userId, Guid.Empty, channelId,
            ParseAvailabilityScope(parts.Length > 2 ? parts[2] : string.Empty),
            TryGetDateTime(item, "updated_at") ?? DateTime.UtcNow);
    }

    private static DocumentAvailabilityScope ParseAvailabilityScope(string scopePart)
        => scopePart switch
        {
            "user" => DocumentAvailabilityScope.User,
            "tenant" => DocumentAvailabilityScope.Tenant,
            _ => DocumentAvailabilityScope.Channel,
        };

    private static bool IsReadable(DocumentScopeContext scope, string slug, IReadOnlyList<Guid>? channelIds)
    {
        var parts = slug.Split('/');
        if (parts.Length < 6 || !string.Equals(parts[0], "documents", StringComparison.Ordinal))
        {
            return false;
        }

        if (!Guid.TryParse(parts[1], out var tenantId) || tenantId != scope.TenantId)
        {
            return false;
        }

        if (string.Equals(parts[2], "user", StringComparison.OrdinalIgnoreCase))
        {
            return parts.Length >= 5
                && Guid.TryParse(parts[4], out var userId)
                && userId == scope.UserId;
        }

        if (!string.Equals(parts[2], "channel", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parts[2], "tenant", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (channelIds == null || channelIds.Count == 0)
        {
            return true;
        }

        return parts.Length >= 4
            && Guid.TryParse(parts[3], out var channelId)
            && channelIds.Contains(channelId);
    }

    private static string GetFingerprintFromSlug(string slug)
    {
        var parts = slug.Split('/');
        return parts.Length > 0 ? parts[^1] : slug;
    }

    private static DateTime? TryGetDateTime(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTime.TryParse(
            prop.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed) ? parsed : null;
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