using LeanKernel.Host.Services;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Tests.Unit.Host;

public class FileBrowserServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileBrowserService _service;

    public FileBrowserServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LeanKernel-test-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(_tempDir, "wiki");
        Directory.CreateDirectory(wikiDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "sessions"));

        // Create some test files
        File.WriteAllText(Path.Combine(wikiDir, "test.json"), """{"hello": "world"}""");
        File.WriteAllText(Path.Combine(_tempDir, "config.yaml"), "key: value");

        var config = Options.Create(new LeanKernelConfig { Wiki = new WikiConfig { BasePath = wikiDir } });
        _service = new FileBrowserService(config);
    }

    [Fact]
    public void Browse_Root_ReturnsDirectories()
    {
        var result = _service.Browse(null);
        Assert.Null(result.Error);
        Assert.NotEmpty(result.Entries);
        // Root is the data dir (parent of wiki), should contain wiki/ and sessions/
        Assert.Contains(result.Entries, e => e.Name == "wiki" && e.IsDirectory);
        Assert.Contains(result.Entries, e => e.Name == "sessions" && e.IsDirectory);
    }

    [Fact]
    public void Browse_Wiki_ReturnsFiles()
    {
        var result = _service.Browse("wiki");
        Assert.Null(result.Error);
        Assert.Contains(result.Entries, e => e.Name == "test.json" && !e.IsDirectory);
    }

    [Fact]
    public void Browse_PathTraversal_ReturnsError()
    {
        var result = _service.Browse("../../etc");
        Assert.Equal("Directory not found", result.Error);
    }

    [Fact]
    public async Task Read_ValidFile_ReturnsContent()
    {
        var result = await _service.ReadAsync("wiki/test.json", CancellationToken.None);
        Assert.Null(result.Error);
        Assert.Contains("hello", result.Content);
    }

    [Fact]
    public async Task Read_NonExistent_ReturnsError()
    {
        var result = await _service.ReadAsync("nonexistent.txt", CancellationToken.None);
        Assert.Equal("File not found", result.Error);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
