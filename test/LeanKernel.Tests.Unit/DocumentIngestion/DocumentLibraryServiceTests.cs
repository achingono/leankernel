using System.IO.Compression;

using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.DocumentIngestion;

public sealed class DocumentLibraryServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly Mock<IDocumentStoreClient> _storeMock;
    private readonly DocumentLibraryService _service;

    public DocumentLibraryServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);

        _storeMock = new Mock<IDocumentStoreClient>();
        var fileSettings = Options.Create(new FileSettings { RootPath = _tempRoot });
        _service = new DocumentLibraryService(_storeMock.Object, fileSettings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }

    [Fact]
    public async Task IngestDocumentAsync_FileNotFound_ReturnsFailure()
    {
        var job = CreateJob("/nonexistent/file.txt");

        var result = await _service.IngestDocumentAsync(job);

        result.Success.Should().BeFalse();
        result.IsDuplicate.Should().BeFalse();
        result.Fingerprint.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestDocumentAsync_Duplicate_ReturnsDuplicate()
    {
        var file = CreateTempFile("hello world");
        var job = CreateJob(file);

        _storeMock
            .Setup(s => s.ExistsAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.IngestDocumentAsync(job);

        result.Success.Should().BeTrue();
        result.IsDuplicate.Should().BeTrue();
        result.Fingerprint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task IngestDocumentAsync_NewDocument_IngestsAndUpserts()
    {
        var file = CreateTempFile("unique content");
        var job = CreateJob(file);

        _storeMock
            .Setup(s => s.ExistsAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _storeMock
            .Setup(s => s.UpsertAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<DocumentCatalogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _service.IngestDocumentAsync(job);

        result.Success.Should().BeTrue();
        result.IsDuplicate.Should().BeFalse();
        result.Fingerprint.Should().NotBeNullOrEmpty();

        _storeMock.Verify(s => s.UpsertAsync(
            It.IsAny<DocumentScopeContext>(),
            result.Fingerprint,
            It.Is<DocumentCatalogEntry>(e => e.FileName == job.FileName),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IngestDocumentAsync_StoresFileInCorrectHierarchicalPath()
    {
        var content = "stored file test";
        var file = CreateTempFile(content);
        var job = CreateJob(file);

        _storeMock
            .Setup(s => s.ExistsAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _storeMock
            .Setup(s => s.UpsertAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<DocumentCatalogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _service.IngestDocumentAsync(job);

        result.Success.Should().BeTrue();
        var fingerprint = result.Fingerprint;
        var prefix1 = fingerprint[..2];
        var prefix2 = fingerprint[2..4];
        var expectedDir = Path.Combine(
            _tempRoot, "documents",
            job.TenantId.ToString(),
            job.AvailabilityScope.ToString().ToLowerInvariant(),
            job.ChannelId.ToString(),
            job.UserId.ToString(),
            prefix1,
            prefix2);
        var expectedPath = Path.Combine(expectedDir, job.FileName);

        File.Exists(expectedPath).Should().BeTrue();
        var storedContent = await File.ReadAllTextAsync(expectedPath);
        storedContent.Should().Be(content);
    }

    [Fact]
    public async Task IngestDocumentAsync_TextFile_ExtractsText()
    {
        var file = CreateTempFile("extracted text content");
        var job = CreateJob(file);

        _storeMock
            .Setup(s => s.ExistsAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DocumentCatalogEntry? capturedEntry = null;
        _storeMock
            .Setup(s => s.UpsertAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<DocumentCatalogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentScopeContext, string, DocumentCatalogEntry, CancellationToken>((_, _, e, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        await _service.IngestDocumentAsync(job);

        capturedEntry.Should().NotBeNull();
        capturedEntry!.ExtractedText.Should().Be("extracted text content");
    }

    [Fact]
    public async Task IngestDocumentAsync_JsonFile_ExtractsText()
    {
        var file = CreateTempFile("""{"key": "value"}""", ".json");
        var job = CreateJob(file);

        _storeMock
            .Setup(s => s.ExistsAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DocumentCatalogEntry? capturedEntry = null;
        _storeMock
            .Setup(s => s.UpsertAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<DocumentCatalogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentScopeContext, string, DocumentCatalogEntry, CancellationToken>((_, _, e, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        await _service.IngestDocumentAsync(job);

        capturedEntry.Should().NotBeNull();
        capturedEntry!.ExtractedText.Should().Be("""{"key": "value"}""");
    }

    [Fact]
    public async Task IngestDocumentAsync_BinaryFile_ReturnsEmptyText()
    {
        var file = CreateTempFile(new byte[] { 0x00, 0x01, 0x02 }, ".bin");
        var job = CreateJob(file);

        _storeMock
            .Setup(s => s.ExistsAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DocumentCatalogEntry? capturedEntry = null;
        _storeMock
            .Setup(s => s.UpsertAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<DocumentCatalogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentScopeContext, string, DocumentCatalogEntry, CancellationToken>((_, _, e, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        await _service.IngestDocumentAsync(job);

        capturedEntry.Should().NotBeNull();
        capturedEntry!.ExtractedText.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestDocumentAsync_PdfFile_ReturnsEmptyText()
    {
        var file = CreateTempFile("%PDF-1.4 fake content", ".pdf");
        var job = CreateJob(file);

        _storeMock
            .Setup(s => s.ExistsAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DocumentCatalogEntry? capturedEntry = null;
        _storeMock
            .Setup(s => s.UpsertAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<DocumentCatalogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentScopeContext, string, DocumentCatalogEntry, CancellationToken>((_, _, e, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        await _service.IngestDocumentAsync(job);

        capturedEntry.Should().NotBeNull();
        capturedEntry!.ExtractedText.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestDocumentAsync_DocxFile_ExtractsTextViaTextExtractionHelper()
    {
        var file = CreateDocxFile("This is docx");
        var job = CreateJob(file);

        _storeMock
            .Setup(s => s.ExistsAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DocumentCatalogEntry? capturedEntry = null;
        _storeMock
            .Setup(s => s.UpsertAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<DocumentCatalogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentScopeContext, string, DocumentCatalogEntry, CancellationToken>((_, _, e, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        await _service.IngestDocumentAsync(job);

        capturedEntry.Should().NotBeNull();
        capturedEntry!.ExtractedText.Should().Contain("This is docx");
    }

    [Fact]
    public async Task IngestDocumentAsync_XlsxFile_ExtractsText()
    {
        var file = CreateXlsxFile("xlsx value 42");
        var job = CreateJob(file);

        _storeMock
            .Setup(s => s.ExistsAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DocumentCatalogEntry? capturedEntry = null;
        _storeMock
            .Setup(s => s.UpsertAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<DocumentCatalogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentScopeContext, string, DocumentCatalogEntry, CancellationToken>((_, _, e, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        await _service.IngestDocumentAsync(job);

        capturedEntry.Should().NotBeNull();
        capturedEntry!.ExtractedText.Should().Contain("xlsx value 42");
    }

    [Fact]
    public async Task IngestDocumentAsync_PythonUnavailable_ReturnsEmptyText()
    {
        var library = CreateLibraryWithPythonExecutable("nonexistent-python-binary");
        var file = CreateDocxFile("This is docx");
        var job = CreateJob(file);

        _storeMock
            .Setup(s => s.ExistsAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DocumentCatalogEntry? capturedEntry = null;
        _storeMock
            .Setup(s => s.UpsertAsync(It.IsAny<DocumentScopeContext>(), It.IsAny<string>(), It.IsAny<DocumentCatalogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentScopeContext, string, DocumentCatalogEntry, CancellationToken>((_, _, e, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        await library.IngestDocumentAsync(job);

        capturedEntry.Should().NotBeNull();
        capturedEntry!.ExtractedText.Should().BeEmpty();
    }

    private string CreateDocxFile(string text)
    {
        var path = Path.Combine(_tempRoot, $"{Guid.NewGuid():N}.docx");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("word/document.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write($$"""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>{{text}}</w:t></w:r></w:p></w:body></w:document>""");
        return path;
    }

    private string CreateXlsxFile(string value)
    {
        var path = Path.Combine(_tempRoot, $"{Guid.NewGuid():N}.xlsx");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("xl/worksheets/sheet1.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write($$"""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>{{value}}</t></is></c></row></sheetData></worksheet>""");
        return path;
    }

    private DocumentLibraryService CreateLibraryWithPythonExecutable(string pythonExecutable)
        => new(
            _storeMock.Object,
            Options.Create(new FileSettings
            {
                RootPath = _tempRoot,
                PythonExecutable = pythonExecutable,
            }));

    private string CreateTempFile(string content, string extension = ".txt")
    {
        var path = Path.Combine(_tempRoot, $"{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateTempFile(byte[] content, string extension)
    {
        var path = Path.Combine(_tempRoot, $"{Guid.NewGuid()}{extension}");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static DocumentIngestionJob CreateJob(string filePath) => new(
        filePath,
        Path.GetFileName(filePath),
        "text/plain",
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        DocumentAvailabilityScope.User,
        DocumentIngestionSource.Upload);
}