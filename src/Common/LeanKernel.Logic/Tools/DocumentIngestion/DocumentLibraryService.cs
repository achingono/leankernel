using System.Security.Cryptography;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;

using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.DocumentIngestion;

/// <summary>
/// Stores documents on disk and in the document catalog, with deduplication by content fingerprint.
/// </summary>
public sealed class DocumentLibraryService : IDocumentLibraryService
{
    private readonly IDocumentStoreClient _storeClient;
    private readonly string _documentsRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentLibraryService"/> class.
    /// </summary>
    /// <param name="storeClient">The document store client.</param>
    /// <param name="fileSettings">The file settings providing the storage root path.</param>
    public DocumentLibraryService(IDocumentStoreClient storeClient, IOptions<FileSettings> fileSettings)
    {
        _storeClient = storeClient;
        var root = fileSettings.Value.RootPath;
        _documentsRoot = Path.Combine(string.IsNullOrEmpty(root) ? Directory.GetCurrentDirectory() : root, "documents");
        Directory.CreateDirectory(_documentsRoot);
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

    private static async Task<string> ExtractTextAsync(string filePath, CancellationToken ct)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".html" or ".yaml" or ".yml" =>
                await File.ReadAllTextAsync(filePath, ct),

            ".pdf" => await ExtractPdfTextAsync(filePath, ct),

            _ => string.Empty,
        };
    }

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
