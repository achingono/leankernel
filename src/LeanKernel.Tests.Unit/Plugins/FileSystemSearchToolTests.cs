using System.Text.Json;
using LeanKernel.Plugins.BuiltIn;

namespace LeanKernel.Tests.Unit.Plugins;

public class FileSystemSearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_FindsFileByName()
    {
        var tmpDir = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(tmpDir, "documents", "sermons"));
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "documents", "sermons", "The Lion And The Donkey"), "sermon text");

        try
        {
            var tool = new FileSystemSearchTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"query":"The Lion and The Donkey","path":"documents","limit":5}""",
                CancellationToken.None);

            Assert.True(result.Success);
            using var doc = JsonDocument.Parse(result.Output!);
            var results = doc.RootElement.GetProperty("Results").EnumerateArray().ToList();
            Assert.Single(results);
            Assert.Equal("documents/sermons/The Lion And The Donkey", results[0].GetProperty("Path").GetString());
            Assert.Contains("name", results[0].GetProperty("MatchedOn").EnumerateArray().Select(x => x.GetString()));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_FindsFileByContent()
    {
        var tmpDir = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(tmpDir, "documents"));
        await File.WriteAllTextAsync(
            Path.Combine(tmpDir, "documents", "notes.md"),
            "This profile mentions cloud architecture and product leadership.");

        try
        {
            var tool = new FileSystemSearchTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"content":"product leadership","path":"documents"}""",
                CancellationToken.None);

            Assert.True(result.Success);
            using var doc = JsonDocument.Parse(result.Output!);
            var match = Assert.Single(doc.RootElement.GetProperty("Results").EnumerateArray());
            Assert.Equal("documents/notes.md", match.GetProperty("Path").GetString());
            Assert.Contains("content", match.GetProperty("MatchedOn").EnumerateArray().Select(x => x.GetString()));
            Assert.Contains("product leadership", match.GetProperty("Snippet").GetString());
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_AcceptsPlainTextQuery()
    {
        var tmpDir = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "profile.md"), "profile");

        try
        {
            var tool = new FileSystemSearchTool(tmpDir);
            var result = await tool.ExecuteAsync("profile", CancellationToken.None);

            Assert.True(result.Success);
            using var doc = JsonDocument.Parse(result.Output!);
            var match = Assert.Single(doc.RootElement.GetProperty("Results").EnumerateArray());
            Assert.Equal("profile.md", match.GetProperty("Path").GetString());
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnderspecifiedSearch_PrefersSourceDocumentCandidateOverGeneratedArtifact()
    {
        var tmpDir = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(tmpDir, "documents"));
        Directory.CreateDirectory(Path.Combine(tmpDir, "wiki", "llm"));
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "documents", "Alfero Chingono.pdf"), "%PDF");
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "wiki", "llm", "background-20260510.md"), "generated summary");

        try
        {
            var tool = new FileSystemSearchTool(tmpDir);
            var result = await tool.ExecuteAsync("""{"query":"background","limit":5}""", CancellationToken.None);

            Assert.True(result.Success);
            using var doc = JsonDocument.Parse(result.Output!);
            var results = doc.RootElement.GetProperty("Results").EnumerateArray().ToList();
            Assert.NotEmpty(results);
            Assert.Equal("documents/Alfero Chingono.pdf", results[0].GetProperty("Path").GetString());
            Assert.Contains(
                "source_document_candidate",
                results[0].GetProperty("MatchedOn").EnumerateArray().Select(x => x.GetString()));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_FindsNamedPdfAndDocxDocumentsExactly()
    {
        var tmpDir = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(tmpDir, "documents", "profiles"));
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "documents", "profiles", "Alfero Profile.pdf"), "%PDF");
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "documents", "profiles", "Strengths Report.docx"), "docx bytes");

        try
        {
            var tool = new FileSystemSearchTool(tmpDir);
            var pdfResult = await tool.ExecuteAsync(
                """{"query":"Alfero Profile.pdf","path":"documents","limit":5}""",
                CancellationToken.None);
            var docxResult = await tool.ExecuteAsync(
                """{"query":"Strengths Report.docx","path":"documents","limit":5}""",
                CancellationToken.None);

            Assert.True(pdfResult.Success);
            Assert.True(docxResult.Success);

            using var pdfDoc = JsonDocument.Parse(pdfResult.Output!);
            using var docxDoc = JsonDocument.Parse(docxResult.Output!);
            Assert.Equal("documents/profiles/Alfero Profile.pdf", pdfDoc.RootElement.GetProperty("Results")[0].GetProperty("Path").GetString());
            Assert.Equal("documents/profiles/Strengths Report.docx", docxDoc.RootElement.GetProperty("Results")[0].GetProperty("Path").GetString());
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BlocksPathTraversal()
    {
        var tmpDir = CreateTempDirectory();

        try
        {
            var tool = new FileSystemSearchTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"query":"passwd","path":"../../etc"}""",
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
    public async Task ExecuteAsync_RequiresSearchTerm()
    {
        var tmpDir = CreateTempDirectory();

        try
        {
            var tool = new FileSystemSearchTool(tmpDir);
            var result = await tool.ExecuteAsync("""{"path":"documents"}""", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("required", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        return tmpDir;
    }
}
