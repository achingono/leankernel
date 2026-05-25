using System.Text.Json;
using LeanKernel.Abstractions.Models;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Gateway.Services;

/// <summary>
/// Provides UI-friendly knowledge browser operations over the underlying knowledge services.
/// </summary>
public sealed class KnowledgeUiService(
    IKnowledgeService knowledgeService,
    ILogger<KnowledgeUiService> logger,
    IServiceProvider serviceProvider)
{
    private static readonly string[] DiscoveryQueries = ["wiki", "knowledge", "identity", "learning", "context"];

    private readonly IKnowledgeService _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    private readonly ILogger<KnowledgeUiService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly Dictionary<string, KnowledgePageSummary> _knownPages = new(StringComparer.OrdinalIgnoreCase);

    private GBrainMcpClient? _gBrainClient;
    private bool? _supportsListPages;

    /// <summary>
    /// Searches knowledge pages and returns UI-friendly result models.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">The maximum number of results to return.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A list of search results suitable for the knowledge browser.</returns>
    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchPagesAsync(
        string query,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var trimmedQuery = query.Trim();
        var results = await _knowledgeService.SearchAsync(trimmedQuery, maxResults, ct).ConfigureAwait(false);
        var mappedResults = new List<KnowledgeSearchResult>(results.Count);

        foreach (var result in results)
        {
            var searchResult = new KnowledgeSearchResult
            {
                Slug = result.Key,
                Score = result.Score,
                Preview = BuildPreview(result.Content),
                Content = result.Content
            };

            mappedResults.Add(searchResult);
            RememberPage(new KnowledgePageSummary
            {
                Slug = result.Key,
                LastModified = TryReadDateTimeOffset(result.Metadata, "updated_at", "last_modified", "lastModified"),
                TagCount = 0,
                Tags = []
            });
        }

        return mappedResults;
    }

    /// <summary>
    /// Gets a knowledge page and enriches it with additional metadata when available.
    /// </summary>
    /// <param name="slug">The knowledge page slug.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The enriched knowledge page detail, or <see langword="null"/> when the page does not exist.</returns>
    public async Task<KnowledgePageDetail?> GetPageDetailAsync(string slug, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var page = await _knowledgeService.GetPageAsync(slug, ct).ConfigureAwait(false);
        if (page is null)
        {
            return null;
        }

        var detail = new KnowledgePageDetail
        {
            Slug = page.Key,
            Content = page.Content,
            LastModified = page.LastModified,
            Tags = [],
            LinkedPages = NormalizeLinks(page.LinkedPages)
        };

        var gBrainClient = GetGBrainClient();
        if (gBrainClient is not null)
        {
            try
            {
                var enrichedPage = await gBrainClient.CallToolAsync("get_page", new { slug = page.Key }, ct).ConfigureAwait(false);
                if (enrichedPage is { } enrichedPageValue)
                {
                    detail = Merge(detail, ParsePageDetail(enrichedPageValue, page.Key));
                }
            }
            catch (Exception ex) when (ex is GBrainException or HttpRequestException or JsonException)
            {
                _logger.LogDebug(ex, "Knowledge metadata enrichment failed for {Slug}", page.Key);
            }
        }

        RememberPage(detail);
        return detail;
    }

    /// <summary>
    /// Saves a knowledge page.
    /// </summary>
    /// <param name="slug">The knowledge page slug.</param>
    /// <param name="content">The raw page content.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the page has been written.</returns>
    public async Task SavePageAsync(string slug, string content, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentNullException.ThrowIfNull(content);

        var trimmedSlug = slug.Trim();
        await _knowledgeService.PutPageAsync(trimmedSlug, content, ct).ConfigureAwait(false);

        RememberPage(new KnowledgePageSummary
        {
            Slug = trimmedSlug,
            LastModified = DateTimeOffset.UtcNow,
            TagCount = 0,
            Tags = []
        });
    }

    /// <summary>
    /// Browses knowledge pages using the richer MCP page listing when available.
    /// </summary>
    /// <param name="pageNumber">The 1-based page number.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="sort">The desired sort order.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A browse result for the requested page.</returns>
    public async Task<KnowledgeBrowseResult> BrowsePagesAsync(
        int pageNumber,
        int pageSize,
        KnowledgeBrowseSort sort,
        CancellationToken ct = default)
    {
        var normalizedPageNumber = Math.Max(1, pageNumber);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var gBrainClient = GetGBrainClient();

        if (gBrainClient is null)
        {
            await DiscoverPagesFromSearchAsync(ct).ConfigureAwait(false);
            return BuildBrowseFromKnownPages(
                normalizedPageNumber,
                normalizedPageSize,
                sort,
                "Knowledge page listing is unavailable; showing pages discovered in this browser session.");
        }

        if (!await SupportsListPagesAsync(gBrainClient, ct).ConfigureAwait(false))
        {
            await DiscoverPagesFromSearchAsync(ct).ConfigureAwait(false);
            return BuildBrowseFromKnownPages(
                normalizedPageNumber,
                normalizedPageSize,
                sort,
                "The connected knowledge provider does not expose page listing; showing pages discovered in this browser session.");
        }

        try
        {
            var result = await gBrainClient.CallToolAsync(
                    "list_pages",
                    new
                    {
                        page = normalizedPageNumber,
                        limit = normalizedPageSize,
                        page_size = normalizedPageSize,
                        offset = (normalizedPageNumber - 1) * normalizedPageSize,
                        sort = GetSortValue(sort)
                    },
                    ct)
                .ConfigureAwait(false);

            if (result is null)
            {
                return new KnowledgeBrowseResult
                {
                    Items = [],
                    PageNumber = normalizedPageNumber,
                    PageSize = normalizedPageSize,
                    TotalCount = 0,
                    Sort = sort,
                    IsDegraded = false,
                    StatusMessage = "No knowledge pages were returned by the provider."
                };
            }

            var browseResult = ParseBrowseResult(result.Value, normalizedPageNumber, normalizedPageSize, sort);
            if (browseResult.Items.Count == 0)
            {
                await DiscoverPagesFromSearchAsync(ct).ConfigureAwait(false);
                return BuildBrowseFromKnownPages(
                    normalizedPageNumber,
                    normalizedPageSize,
                    sort,
                    "The provider returned no pages for this browse request; showing pages discovered in this browser session.");
            }

            foreach (var item in browseResult.Items)
            {
                RememberPage(item);
            }

            return browseResult;
        }
        catch (Exception ex) when (ex is GBrainException or HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Knowledge page listing failed; falling back to session cache.");
            await DiscoverPagesFromSearchAsync(ct).ConfigureAwait(false);
            return BuildBrowseFromKnownPages(
                normalizedPageNumber,
                normalizedPageSize,
                sort,
                "Knowledge page listing is currently unavailable; showing pages discovered in this browser session.");
        }
    }

    private async Task DiscoverPagesFromSearchAsync(CancellationToken ct)
    {
        if (_knownPages.Count > 0)
        {
            return;
        }

        foreach (var query in DiscoveryQueries)
        {
            IReadOnlyList<RetrievalCandidate> candidates;
            try
            {
                candidates = await _knowledgeService.SearchAsync(query, 20, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is GBrainException or HttpRequestException or JsonException)
            {
                _logger.LogDebug(ex, "Knowledge fallback discovery search failed for query {Query}", query);
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.Key))
                {
                    continue;
                }

                RememberPage(new KnowledgePageSummary
                {
                    Slug = candidate.Key,
                    LastModified = TryReadDateTimeOffset(candidate.Metadata, "updated_at", "last_modified", "lastModified"),
                    TagCount = 0,
                    Tags = []
                });
            }
        }
    }

    private GBrainMcpClient? GetGBrainClient()
        => _gBrainClient ??= _serviceProvider.GetService<GBrainMcpClient>();

    private async Task<bool> SupportsListPagesAsync(GBrainMcpClient gBrainClient, CancellationToken ct)
    {
        if (_supportsListPages.HasValue)
        {
            return _supportsListPages.Value;
        }

        try
        {
            var tools = await gBrainClient.ListToolsAsync(ct).ConfigureAwait(false);
            _supportsListPages = tools.Any(tool => string.Equals(tool.Name, "list_pages", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is GBrainException or HttpRequestException or JsonException)
        {
            _logger.LogDebug(ex, "Unable to inspect GBrain tool list for page browsing support.");
            _supportsListPages = false;
        }

        return _supportsListPages.Value;
    }

    private KnowledgeBrowseResult BuildBrowseFromKnownPages(
        int pageNumber,
        int pageSize,
        KnowledgeBrowseSort sort,
        string statusMessage)
    {
        var orderedItems = OrderPages(_knownPages.Values, sort).ToList();
        var pagedItems = orderedItems
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new KnowledgeBrowseResult
        {
            Items = pagedItems,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = orderedItems.Count,
            Sort = sort,
            IsDegraded = true,
            StatusMessage = statusMessage
        };
    }

    private void RememberPage(KnowledgePageDetail detail)
        => RememberPage(new KnowledgePageSummary
        {
            Slug = detail.Slug,
            LastModified = detail.LastModified,
            TagCount = detail.Tags.Count,
            Tags = detail.Tags
        });

    private void RememberPage(KnowledgePageSummary summary)
    {
        if (_knownPages.TryGetValue(summary.Slug, out var existing))
        {
            _knownPages[summary.Slug] = new KnowledgePageSummary
            {
                Slug = summary.Slug,
                LastModified = summary.LastModified ?? existing.LastModified,
                TagCount = summary.TagCount > 0 ? summary.TagCount : existing.TagCount,
                Tags = summary.Tags.Count > 0 ? summary.Tags : existing.Tags
            };

            return;
        }

        _knownPages[summary.Slug] = summary;
    }

    private static KnowledgeBrowseResult ParseBrowseResult(
        JsonElement result,
        int pageNumber,
        int pageSize,
        KnowledgeBrowseSort sort)
    {
        var itemsElement = TryGetCollectionElement(result, out var collection)
            ? collection
            : result;
        var items = new List<KnowledgePageSummary>();

        if (itemsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsElement.EnumerateArray())
            {
                var summary = ParsePageSummary(item);
                if (summary is not null)
                {
                    items.Add(summary);
                }
            }
        }

        var totalCount = TryGetInt(result, "total_count", "total", "count")
            ?? TryGetNestedInt(result, "pagination", "total_count", "total", "count")
            ?? 0;

        return new KnowledgeBrowseResult
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            Sort = sort,
            IsDegraded = false,
            StatusMessage = items.Count == 0 ? "No knowledge pages matched the current browse request." : null
        };
    }

    private static KnowledgePageSummary? ParsePageSummary(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var slug = TryGetString(item, "slug", "key", "name", "path")
            ?? TryGetNumericString(item, "id");
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var tags = ExtractStringList(item, "tags");
        var tagCount = TryGetInt(item, "tag_count", "tagCount") ?? tags.Count;

        return new KnowledgePageSummary
        {
            Slug = slug,
            LastModified = TryGetDateTimeOffset(item, "updated_at", "last_modified", "lastModified"),
            TagCount = tagCount,
            Tags = tags
        };
    }

    private static KnowledgePageDetail ParsePageDetail(JsonElement result, string fallbackSlug)
    {
        var pageElement = TryGetNestedElement(result, out var nestedElement)
            ? nestedElement
            : result;
        var slug = TryGetString(pageElement, "slug", "key") ?? fallbackSlug;
        var content = TryGetString(pageElement, "compiled_truth", "content") ?? string.Empty;

        return new KnowledgePageDetail
        {
            Slug = slug,
            Content = content,
            LastModified = TryGetDateTimeOffset(pageElement, "updated_at", "last_modified", "lastModified"),
            Tags = ExtractStringList(pageElement, "tags"),
            LinkedPages = ExtractLinkedPages(pageElement)
        };
    }

    private static KnowledgePageDetail Merge(KnowledgePageDetail basePage, KnowledgePageDetail enrichedPage)
        => new()
        {
            Slug = enrichedPage.Slug,
            Content = string.IsNullOrWhiteSpace(enrichedPage.Content) ? basePage.Content : enrichedPage.Content,
            LastModified = enrichedPage.LastModified ?? basePage.LastModified,
            Tags = enrichedPage.Tags.Count > 0 ? enrichedPage.Tags : basePage.Tags,
            LinkedPages = enrichedPage.LinkedPages.Count > 0 ? enrichedPage.LinkedPages : basePage.LinkedPages
        };

    private static IEnumerable<KnowledgePageSummary> OrderPages(IEnumerable<KnowledgePageSummary> pages, KnowledgeBrowseSort sort)
        => sort == KnowledgeBrowseSort.Alphabetical
            ? pages.OrderBy(page => page.Slug, StringComparer.OrdinalIgnoreCase)
            : pages
                .OrderByDescending(page => page.LastModified ?? DateTimeOffset.MinValue)
                .ThenBy(page => page.Slug, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> NormalizeLinks(IReadOnlyList<string>? links)
        => links?
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(link => link, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];

    private static IReadOnlyList<string> ExtractLinkedPages(JsonElement item)
    {
        if (!TryGetProperty(item, "links", out var linksElement) || linksElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var linkedPages = new List<string>();
        foreach (var link in linksElement.EnumerateArray())
        {
            if (link.ValueKind == JsonValueKind.String)
            {
                var slug = link.GetString();
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    linkedPages.Add(slug);
                }

                continue;
            }

            if (link.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var slugValue = TryGetString(link, "to_slug", "slug", "key");
            if (!string.IsNullOrWhiteSpace(slugValue))
            {
                linkedPages.Add(slugValue);
            }
        }

        return linkedPages
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(link => link, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ExtractStringList(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(item, propertyName, out var propertyValue) || propertyValue.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return propertyValue
                .EnumerateArray()
                .Where(static value => value.ValueKind == JsonValueKind.String)
                .Select(static value => value.GetString())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return [];
    }

    private static string BuildPreview(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "No preview available.";
        }

        var normalized = string.Join(
            ' ',
            content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalized.Length <= 200
            ? normalized
            : normalized[..200] + "…";
    }

    private static DateTimeOffset? TryReadDateTimeOffset(
        IReadOnlyDictionary<string, string>? metadata,
        params string[] keys)
    {
        if (metadata is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (DateTimeOffset.TryParse(value, out var parsedValue))
            {
                return parsedValue;
            }
        }

        return null;
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(item, propertyName, out var propertyValue))
            {
                continue;
            }

            if (propertyValue.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(propertyValue.GetString(), out var parsedStringValue))
            {
                return parsedStringValue;
            }
        }

        return null;
    }

    private static int? TryGetInt(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(item, propertyName, out var propertyValue))
            {
                continue;
            }

            if (propertyValue.ValueKind == JsonValueKind.Number && propertyValue.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (propertyValue.ValueKind == JsonValueKind.String && int.TryParse(propertyValue.GetString(), out var parsedInt))
            {
                return parsedInt;
            }
        }

        return null;
    }

    private static int? TryGetNestedInt(JsonElement item, string nestedPropertyName, params string[] propertyNames)
    {
        if (!TryGetProperty(item, nestedPropertyName, out var nestedItem))
        {
            return null;
        }

        return TryGetInt(nestedItem, propertyNames);
    }

    private static string? TryGetNumericString(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(item, propertyName, out var propertyValue))
            {
                continue;
            }

            if (propertyValue.ValueKind == JsonValueKind.Number)
            {
                return propertyValue.GetRawText();
            }
        }

        return null;
    }

    private static string? TryGetString(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(item, propertyName, out var propertyValue)
                || propertyValue.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            return propertyValue.GetString();
        }

        return null;
    }

    private static bool TryGetCollectionElement(JsonElement item, out JsonElement collectionElement)
    {
        foreach (var propertyName in new[] { "items", "results", "pages" })
        {
            if (TryGetProperty(item, propertyName, out collectionElement))
            {
                return true;
            }
        }

        if (TryGetProperty(item, "data", out var dataElement))
        {
            if (dataElement.ValueKind == JsonValueKind.Array)
            {
                collectionElement = dataElement;
                return true;
            }

            foreach (var propertyName in new[] { "items", "results", "pages" })
            {
                if (TryGetProperty(dataElement, propertyName, out collectionElement))
                {
                    return true;
                }
            }
        }

        collectionElement = default;
        return false;
    }

    private static bool TryGetNestedElement(JsonElement item, out JsonElement nestedElement)
    {
        foreach (var propertyName in new[] { "page", "item", "result", "data" })
        {
            if (TryGetProperty(item, propertyName, out nestedElement) && nestedElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        nestedElement = default;
        return false;
    }

    private static bool TryGetProperty(JsonElement item, string propertyName, out JsonElement propertyValue)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in item.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    propertyValue = property.Value;
                    return true;
                }
            }
        }

        propertyValue = default;
        return false;
    }

    private static string GetSortValue(KnowledgeBrowseSort sort)
        => sort == KnowledgeBrowseSort.Alphabetical ? "alphabetical" : "recently_modified";
}

/// <summary>
/// Defines the available browse sort modes for knowledge pages.
/// </summary>
public enum KnowledgeBrowseSort
{
    /// <summary>
    /// Sort pages by the most recently modified first.
    /// </summary>
    RecentlyModified,

    /// <summary>
    /// Sort pages alphabetically by slug.
    /// </summary>
    Alphabetical
}

/// <summary>
/// Represents a knowledge search result in the browser UI.
/// </summary>
public sealed record KnowledgeSearchResult
{
    /// <summary>
    /// Gets the page slug.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// Gets the relevance score returned by the knowledge provider.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Gets the trimmed preview text.
    /// </summary>
    public required string Preview { get; init; }

    /// <summary>
    /// Gets the raw content returned for the search result.
    /// </summary>
    public required string Content { get; init; }
}

/// <summary>
/// Represents a browsable knowledge page summary.
/// </summary>
public sealed record KnowledgePageSummary
{
    /// <summary>
    /// Gets the page slug.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// Gets the last modified timestamp when available.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>
    /// Gets the number of tags associated with the page.
    /// </summary>
    public int TagCount { get; init; }

    /// <summary>
    /// Gets the page tags when available.
    /// </summary>
    public required IReadOnlyList<string> Tags { get; init; }
}

/// <summary>
/// Represents the selected knowledge page detail.
/// </summary>
public sealed record KnowledgePageDetail
{
    /// <summary>
    /// Gets the page slug.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// Gets the raw page content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets the last modified timestamp when available.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>
    /// Gets the page tags.
    /// </summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>
    /// Gets the linked page slugs.
    /// </summary>
    public required IReadOnlyList<string> LinkedPages { get; init; }
}

/// <summary>
/// Represents a page of browse results for the knowledge browser.
/// </summary>
public sealed record KnowledgeBrowseResult
{
    /// <summary>
    /// Gets the page summaries returned for the current browse request.
    /// </summary>
    public required IReadOnlyList<KnowledgePageSummary> Items { get; init; }

    /// <summary>
    /// Gets the current 1-based page number.
    /// </summary>
    public required int PageNumber { get; init; }

    /// <summary>
    /// Gets the page size used for the browse request.
    /// </summary>
    public required int PageSize { get; init; }

    /// <summary>
    /// Gets the total number of matching pages when the provider returns it.
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// Gets the sort mode used for the browse request.
    /// </summary>
    public required KnowledgeBrowseSort Sort { get; init; }

    /// <summary>
    /// Gets a value indicating whether the result came from a degraded fallback path.
    /// </summary>
    public bool IsDegraded { get; init; }

    /// <summary>
    /// Gets the optional status message associated with the browse request.
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// Gets a value indicating whether a previous page is available.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Gets a value indicating whether a next page is available.
    /// </summary>
    public bool HasNextPage => TotalCount > PageNumber * PageSize || (TotalCount == 0 && Items.Count == PageSize);
}
