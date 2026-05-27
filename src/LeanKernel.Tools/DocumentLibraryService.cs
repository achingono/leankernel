using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Knowledge;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tools;

/// <summary>
/// Provides high-quality document ingestion by parsing files, compiling markdown wiki pages,
/// saving them in GBrain, and associating the original binary assets.
/// </summary>
public sealed class DocumentLibraryService
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly GBrainMcpClient _gBrainClient;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<DocumentLibraryService> _logger;

    public DocumentLibraryService(
        IKnowledgeService knowledgeService,
        GBrainMcpClient gBrainClient,
        IOptions<LeanKernelConfig> config,
        ILogger<DocumentLibraryService> logger)
    {
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _gBrainClient = gBrainClient ?? throw new ArgumentNullException(nameof(gBrainClient));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Ingests a rich document into the GBrain knowledge wiki.
    /// </summary>
    /// <param name="filename">The original filename of the document.</param>
    /// <param name="fileStream">The readable stream containing the document binary.</param>
    /// <param name="title">Optional human-readable title.</param>
    /// <param name="tags">The tags to assign to the document.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>An ingestion result describing the newly created page.</returns>
    public async Task<DocumentIngestionResult> IngestDocumentAsync(
        string filename,
        Stream fileStream,
        string? title,
        List<string> tags,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentNullException.ThrowIfNull(tags);

        var fileSystemConfig = _config.FileSystem;
        var documentsDir = Path.Combine(fileSystemConfig.AllowedRoot, "documents");
        Directory.CreateDirectory(documentsDir);

        var cleanFilename = Path.GetFileName(filename);
        var finalTitle = NormalizeYamlScalar(string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(cleanFilename) : title);
        var baseSlug = BuildBaseSlug(finalTitle);
        var pageSlug = await ResolveUniquePageSlugAsync(baseSlug, ct).ConfigureAwait(false);

        var extension = Path.GetExtension(cleanFilename);
        var uniqueFilename = $"{baseSlug}-{Guid.NewGuid().ToString("N")[..8]}{extension}".ToLowerInvariant();
        var targetFullPath = Path.Combine(documentsDir, uniqueFilename);
        var relativePath = Path.Combine("documents", uniqueFilename).Replace("\\", "/", StringComparison.Ordinal);
        var cleanTags = tags
            .Select(static tag => NormalizeYamlScalar(tag))
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var importedAt = DateTimeOffset.UtcNow;

        try
        {
            _logger.LogInformation("Saving uploaded document stream to AllowedRoot path: {Path}", targetFullPath);
            await using (var writeStream = new FileStream(targetFullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await fileStream.CopyToAsync(writeStream, ct).ConfigureAwait(false);
            }

            _logger.LogInformation("Invoking text extraction pipeline for: {Path}", targetFullPath);
            var extractedText = await TextExtractionHelper.ExtractAsync(targetFullPath, fileSystemConfig, ct).ConfigureAwait(false);

            var pendingContent = BuildMarkdownContent(
                finalTitle,
                relativePath,
                null,
                cleanTags,
                importedAt,
                extractedText);

            _logger.LogInformation("Writing compiled document page {Slug} to GBrain", pageSlug);
            await _knowledgeService.PutPageAsync(pageSlug, pendingContent, ct).ConfigureAwait(false);

            _logger.LogInformation("Uploading binary asset to GBrain files store and linking to {Slug}", pageSlug);
            var uploadResult = await _gBrainClient.CallToolAsync(
                "file_upload",
                new
                {
                    path = targetFullPath,
                    page_slug = pageSlug
                },
                ct).ConfigureAwait(false);

            var fileStoragePath = ExtractStoragePath(uploadResult)
                ?? throw new InvalidOperationException("GBrain file_upload response did not include a storage path.");

            if (!string.Equals(fileStoragePath, relativePath, StringComparison.Ordinal))
            {
                var finalizedContent = BuildMarkdownContent(
                    finalTitle,
                    relativePath,
                    fileStoragePath,
                    cleanTags,
                    importedAt,
                    extractedText);

                await _knowledgeService.PutPageAsync(pageSlug, finalizedContent, ct).ConfigureAwait(false);
            }

            return new DocumentIngestionResult
            {
                PageSlug = pageSlug,
                Title = finalTitle,
                ExtractedLength = extractedText.Length,
                RelativeFilePath = relativePath,
                FileStoragePath = fileStoragePath
            };
        }
        catch
        {
            TryDeleteFile(targetFullPath);
            throw;
        }
    }

    private async Task<string> ResolveUniquePageSlugAsync(string baseSlug, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 100; attempt++)
        {
            var suffix = attempt == 1 ? string.Empty : $"-{attempt}";
            var slug = $"doc/{baseSlug}{suffix}";
            var existingPage = await _knowledgeService.GetPageAsync(slug, ct).ConfigureAwait(false);
            if (existingPage is null)
            {
                return slug;
            }
        }

        throw new InvalidOperationException($"Unable to allocate a unique document slug for base '{baseSlug}'.");
    }

    private static string BuildBaseSlug(string title)
    {
        var normalizedTitle = NormalizeYamlScalar(title);
        var baseSlug = Regex.Replace(normalizedTitle.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(baseSlug) ? Guid.NewGuid().ToString("N")[..8] : baseSlug;
    }

    private static string BuildMarkdownContent(
        string title,
        string sourceFile,
        string? storagePath,
        IReadOnlyList<string> tags,
        DateTimeOffset importedAt,
        string extractedText)
    {
        var frontmatterLines = new List<string>
        {
            "---",
            "type: document",
            $"title: {QuoteYamlScalar(title)}",
            $"source_file: {QuoteYamlScalar(sourceFile)}",
            $"imported_at: {QuoteYamlScalar(importedAt.ToString("o"))}",
            $"tags: [{string.Join(", ", tags.Select(QuoteYamlScalar))}]"
        };

        if (!string.IsNullOrWhiteSpace(storagePath))
        {
            frontmatterLines.Add($"storage_path: {QuoteYamlScalar(storagePath)}");
        }

        frontmatterLines.Add("---");
        frontmatterLines.Add(string.Empty);
        frontmatterLines.Add($"# {title}");
        frontmatterLines.Add(string.Empty);
        frontmatterLines.Add(extractedText);

        return string.Join(Environment.NewLine, frontmatterLines);
    }

    private static string QuoteYamlScalar(string value)
        => "\"" + NormalizeYamlScalar(value).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string NormalizeYamlScalar(string value)
        => (value ?? string.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();

    private static string? ExtractStoragePath(JsonElement? uploadResult)
    {
        if (uploadResult is not { } result || result.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryReadStringProperty(result, "path")
               ?? TryReadStringProperty(result, "storage_path");
    }

    private static string? TryReadStringProperty(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception deleteEx)
        {
            _logger.LogError(deleteEx, "Cleanup of failed document ingestion file failed at {Path}", path);
        }
    }
}

/// <summary>
/// Represents the outcome of a document ingestion operation.
/// </summary>
public sealed class DocumentIngestionResult
{
    /// <summary>
    /// Gets the compiled page slug inside the wiki knowledge base.
    /// </summary>
    public required string PageSlug { get; init; }

    /// <summary>
    /// Gets the human-readable document title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the length of the extracted text.
    /// </summary>
    public int ExtractedLength { get; init; }

    /// <summary>
    /// Gets the file path relative to the AllowedRoot.
    /// </summary>
    public required string RelativeFilePath { get; init; }

    /// <summary>
    /// Gets the internal storage path inside GBrain storage.
    /// </summary>
    public required string FileStoragePath { get; init; }
}
