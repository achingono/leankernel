using LeanKernel.Plugins.BuiltIn;

namespace LeanKernel.Tests.Unit.Plugins;

public class FileSystemToolTests
{
    [Fact]
    public async Task ExecuteAsync_BlocksPathTraversal()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            var tool = new FileSystemReadTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"path": "../../etc/passwd"}""",
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("denied", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReadsFile()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var testFile = Path.Combine(tmpDir, "test.txt");
        await File.WriteAllTextAsync(testFile, "Hello World\nLine 2");

        try
        {
            var tool = new FileSystemReadTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"path": "test.txt"}""",
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("Hello World", result.Output!);
            Assert.Contains("Line 2", result.Output!);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_FileNotFound()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            var tool = new FileSystemReadTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"path": "nonexistent.txt"}""",
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("not found", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_MaxLines_Truncates()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var lines = Enumerable.Range(0, 200).Select(i => $"Line {i}");
        File.WriteAllLines(Path.Combine(tmpDir, "big.txt"), lines);

        try
        {
            var tool = new FileSystemReadTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"path": "big.txt", "maxLines": 10}""",
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("more lines", result.Output!);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UsesTextExtractorForSupportedDocuments()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var testFile = Path.Combine(tmpDir, "document.pdf");
        await File.WriteAllTextAsync(testFile, "%PDF raw bytes");
        var extractor = new FakeAttachmentTextExtractionService("Alfero Chingono\nPrincipal Architect");

        try
        {
            var tool = new FileSystemReadTool(tmpDir, extractor);
            var result = await tool.ExecuteAsync(
                """{"path": "document.pdf"}""",
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("Principal Architect", result.Output!);
            Assert.Equal("application/pdf", extractor.ContentType);
            Assert.Equal("document.pdf", extractor.FileName);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesPathCaseInsensitively()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmpDir, "documents"));
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "documents", "Profile.txt"), "case insensitive path");

        try
        {
            var tool = new FileSystemReadTool(tmpDir);
            var result = await tool.ExecuteAsync("""{"path":"Documents/profile.txt"}""", CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("case insensitive path", result.Output!);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DocumentWithoutExtractedText_ReturnsError()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "document.pdf"), "%PDF raw bytes");
        var extractor = new FakeAttachmentTextExtractionService(null);

        try
        {
            var tool = new FileSystemReadTool(tmpDir, extractor);
            var result = await tool.ExecuteAsync("""{"path":"document.pdf"}""", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("No text could be extracted", result.Error!);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DocumentWithoutExtractor_ReturnsExplicitError()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "document.docx"), "raw docx bytes");

        try
        {
            var tool = new FileSystemReadTool(tmpDir);
            var result = await tool.ExecuteAsync("""{"path":"document.docx"}""", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Text extraction is not available", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsError()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            var tool = new FileSystemReadTool(tmpDir);
            var result = await tool.ExecuteAsync("not valid json", CancellationToken.None);
            Assert.False(result.Success);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Name_IsFileRead()
    {
        var tool = new FileSystemReadTool("/tmp");
        Assert.Equal("file_read", tool.Name);
    }

    [Fact]
    public void Description_NotEmpty()
    {
        var tool = new FileSystemReadTool("/tmp");
        Assert.NotEmpty(tool.Description);
    }

    [Fact]
    public void ParametersSchema_ContainsPath()
    {
        var tool = new FileSystemReadTool("/tmp");
        Assert.Contains("path", tool.ParametersSchema);
    }

    private sealed class FakeAttachmentTextExtractionService(string? extractedText) : IAttachmentTextExtractionService
    {
        public string? ContentType { get; private set; }

        public string? FileName { get; private set; }

        public bool CanExtractText(string? contentType, string? fileName) => true;

        public Task<string?> ExtractTextAsync(
            string? contentType,
            string? fileName,
            byte[] bytes,
            CancellationToken ct)
        {
            ContentType = contentType;
            FileName = fileName;
            return Task.FromResult<string?>(extractedText);
        }
    }
}
