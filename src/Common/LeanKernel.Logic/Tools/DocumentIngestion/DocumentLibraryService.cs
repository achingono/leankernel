using System.ComponentModel;
using System.Security.Cryptography;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;
using LeanKernel.Logic.Tools.BuiltIn;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Stores documents on disk and in the document catalog, with deduplication by content fingerprint.
/// </summary>
public sealed class DocumentLibraryService : IDocumentLibraryService
{
    private readonly IDocumentStoreClient _storeClient;
    private readonly ILogger<DocumentLibraryService> _logger;
    private readonly FileSettings _fileSettings;
    private readonly string _documentsRoot;
    private readonly string _scratchRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentLibraryService"/> class.
    /// </summary>
    /// <param name="storeClient">The document store client.</param>
    /// <param name="fileSettings">The file settings providing storage root, scratch root, and extraction settings.</param>
    /// <param name="logger">The logger, or null for a no-op logger.</param>
    public DocumentLibraryService(
        IDocumentStoreClient storeClient,
        IOptions<FileSettings> fileSettings,
        ILogger<DocumentLibraryService>? logger = null)
    {
        _storeClient = storeClient;
        _logger = logger ?? NullLogger<DocumentLibraryService>.Instance;
        _fileSettings = fileSettings.Value;
        var root = _fileSettings.RootPath;
        _documentsRoot = Path.Combine(string.IsNullOrEmpty(root) ? Directory.GetCurrentDirectory() : root, "documents");
        Directory.CreateDirectory(_documentsRoot);
        _scratchRoot = string.IsNullOrWhiteSpace(_fileSettings.ScratchRoot)
            ? Path.Combine(_documentsRoot, "_scratch")
            : _fileSettings.ScratchRoot;
    }

    /// <inheritdoc />
    public async Task<IngestionResult> IngestDocumentAsync(DocumentIngestionJob job, CancellationToken ct = default)
    {
        if (!File.Exists(job.FilePath))
        {
            return new IngestionResult(string.Empty, false, false);
        }

        var fingerprint = await ComputeFingerprintAsync(job.FilePath, ct);
        var scope = new DocumentScopeContext(
            job.TenantId,
            job.UserId,
            job.PersonId,
            job.ChannelId,
            job.AvailabilityScope);

        if (await _storeClient.ExistsAsync(scope, fingerprint, ct))
        {
            return new IngestionResult(fingerprint, true, true);
        }

        var storedPath = await CopyToStorageAsync(job, fingerprint, ct);
        var extractedText = await ExtractTextAsync(job.FilePath, ct);

        var entry = new DocumentCatalogEntry(
            fingerprint,
            job.FileName,
            job.ContentType,
            extractedText,
            job.TenantId,
            job.UserId,
            job.PersonId,
            job.ChannelId,
            job.AvailabilityScope,
            DateTime.UtcNow);

        await _storeClient.UpsertAsync(scope, fingerprint, entry, ct);
        return new IngestionResult(fingerprint, true, false);
    }

    private static async Task<string> ComputeFingerprintAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }

    private static string BuildStoragePath(string documentsRoot, DocumentIngestionJob job, string fingerprint)
    {
        var scopeDir = job.AvailabilityScope.ToString().ToLowerInvariant();
        var prefix1 = fingerprint[..2];
        var prefix2 = fingerprint[2..4];
        return Path.Combine(
            documentsRoot,
            job.TenantId.ToString(),
            scopeDir,
            job.ChannelId.ToString(),
            job.UserId.ToString(),
            prefix1,
            prefix2,
            job.FileName);
    }

    private async Task<string> CopyToStorageAsync(DocumentIngestionJob job, string fingerprint, CancellationToken ct)
    {
        var dest = BuildStoragePath(_documentsRoot, job, fingerprint);
        var dir = Path.GetDirectoryName(dest);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        await using var srcStream = File.OpenRead(job.FilePath);
        await using var dstStream = File.Create(dest);
        await srcStream.CopyToAsync(dstStream, ct);
        return dest;
    }

    private async Task<string> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        switch (ext)
        {
            case ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".html" or ".yaml" or ".yml":
                return await File.ReadAllTextAsync(filePath, ct);

            case ".pdf":
                return await ExtractPdfTextAsync(filePath, ct);

            default:
                return IsOfficeOrPrintableCandidate(filePath)
                    ? await ExtractOfficeTextAsync(filePath, ct)
                    : string.Empty;
        }
    }

    private async Task<string> ExtractOfficeTextAsync(string filePath, CancellationToken ct)
    {
        try
        {
            return await TextExtractionHelper.ExtractAsync(
                filePath,
                _scratchRoot,
                _fileSettings.PythonExecutable,
                _fileSettings.MaxExtractedCharacters,
                ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            _logger.LogWarning(ex, "Failed to extract text from document {FilePath}. Inserting empty extractedText.", filePath);
            return string.Empty;
        }
    }

    private static bool IsOfficeOrPrintableCandidate(string filePath)
        => FileSystemSupport.IsWordOpenXmlCandidate(filePath)
            || FileSystemSupport.IsSpreadsheetOpenXmlCandidate(filePath)
            || FileSystemSupport.IsPresentationOpenXmlCandidate(filePath)
            || FileSystemSupport.IsEpubCandidate(filePath)
            || FileSystemSupport.IsLegacyOfficeBinaryCandidate(filePath);

    private static async Task<string> ExtractPdfTextAsync(string filePath, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            var content = await reader.ReadToEndAsync(ct);
            if (content.StartsWith('%') || content.StartsWith("PDF", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return content;
        }
        catch
        {
            return string.Empty;
        }
    }
}