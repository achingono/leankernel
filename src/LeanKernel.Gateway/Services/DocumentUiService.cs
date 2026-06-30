using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LeanKernel.Abstractions.Models;
using LeanKernel.Knowledge;
using LeanKernel.Tools;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Gateway.Services;

/// <summary>
/// Provides web UI operations for browsing, uploading, and generating download links for document assets.
/// </summary>
public sealed class DocumentUiService
{
    private readonly DocumentLibraryService _documentLibraryService;
    private readonly GBrainMcpClient _gBrainClient;
    private readonly ILogger<DocumentUiService> _logger;

    public DocumentUiService(
        DocumentLibraryService documentLibraryService,
        GBrainMcpClient gBrainClient,
        ILogger<DocumentUiService> logger)
    {
        _documentLibraryService = documentLibraryService ?? throw new ArgumentNullException(nameof(documentLibraryService));
        _gBrainClient = gBrainClient ?? throw new ArgumentNullException(nameof(gBrainClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Forwards file ingestion streams to the underlying DocumentLibraryService.
    /// </summary>
    public async Task<DocumentIngestionResult> IngestDocumentAsync(
        string filename,
        Stream fileStream,
        string? title,
        List<string> tags,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Forwarding ingestion request for document {Filename} to library service.", filename);
        return await _documentLibraryService.IngestDocumentAsync(filename, fileStream, title, tags, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves standard document pages (type = 'document') from the GBrain server.
    /// </summary>
    public async Task<List<KnowledgePageSummary>> BrowseDocumentsAsync(
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var normalizedPageNumber = Math.Max(1, pageNumber);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 200);

        try
        {
            _logger.LogInformation("Querying document pages (type = document) from GBrain MCP.");
            var result = await _gBrainClient.CallToolAsync(
                "list_pages",
                new
                {
                    type = "document",
                    page = normalizedPageNumber,
                    limit = normalizedPageSize,
                    page_size = normalizedPageSize,
                    offset = (normalizedPageNumber - 1) * normalizedPageSize
                },
                ct).ConfigureAwait(false);

            if (result is null)
            {
                return [];
            }

            var items = new List<KnowledgePageSummary>();
            var itemsElement = TryGetProperty(result.Value, "pages", out var pagesProp)
                ? pagesProp
                : result.Value;
            
            if (itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsElement.EnumerateArray())
                {
                    var summary = TryParsePageItem(item);
                    if (summary is not null)
                    {
                        items.Add(summary);
                    }
                }
            }

            return items;
        }
        catch (Exception ex) when (ex is GBrainException or HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to browse documents from GBrain.");
            throw;
        }
    }

    private static KnowledgePageSummary? TryParsePageItem(JsonElement item)
    {
        var slug = TryGetString(item, "slug", "path", "key");
        if (string.IsNullOrEmpty(slug))
        {
            return null;
        }

        var lastModified = TryGetDateTime(item, "updated_at", "updatedAt", "last_modified", "lastModified");
        var tags = ExtractTagsFromItem(item);

        return new KnowledgePageSummary
        {
            Slug = slug,
            LastModified = lastModified,
            TagCount = tags.Count,
            Tags = tags
        };
    }

    private static List<string> ExtractTagsFromItem(JsonElement item)
    {
        var tags = new List<string>();
        if (TryGetProperty(item, "tags", out var tProp) && tProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tProp.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String)
                {
                    tags.Add(tag.GetString()!);
                }
            }
        }

        return tags;
    }

    /// <summary>
    /// Generates a temporary signed download URL for an uploaded file using GBrain's signed url tool.
    /// </summary>
    public async Task<string?> GetDownloadUrlAsync(string storagePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        try
        {
            _logger.LogDebug("Querying signed url from GBrain for: {Path}", storagePath);
            var result = await _gBrainClient.CallToolAsync("file_url", new { storage_path = storagePath }, ct).ConfigureAwait(false);

            if (result is { } res && res.ValueKind == JsonValueKind.Object)
            {
                if (res.TryGetProperty("url", out var urlProp))
                {
                    return urlProp.GetString();
                }
                if (res.TryGetProperty("path", out var pathProp))
                {
                    return pathProp.GetString();
                }
            }

            // Fallback default endpoint representation
            return $"/api/files/download?path={Uri.EscapeDataString(storagePath)}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve signed URL from GBrain for path {Path}.", storagePath);
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static DateTimeOffset? TryGetDateTime(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (DateTimeOffset.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
