using LeanKernel.Host.Services;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Tests.Unit.Host;

public class LogReaderServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LogReaderService _service;

    public LogReaderServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LeanKernel-log-test-{Guid.NewGuid():N}");
        var wikiDir = Path.Combine(_tempDir, "wiki");
        var logDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(wikiDir);
        Directory.CreateDirectory(logDir);

        // Create test log files
        File.WriteAllLines(Path.Combine(logDir, "LeanKernel-20260429.log"), [
            "[09:00:00 INF] LeanKernel engine starting...",
            "[09:00:01 INF] Connected to Qdrant",
            "[09:00:05 WRN] Signal-cli not found",
            "[09:01:00 ERR] Failed to connect to LiteLLM",
            "[09:02:00 DBG] Processing query: hello"
        ]);

        var config = Options.Create(new LeanKernelConfig { Wiki = new WikiConfig { BasePath = wikiDir } });
        _service = new LogReaderService(config);
    }

    [Fact]
    public void ListLogFiles_FindsTestFile()
    {
        var files = _service.ListLogFiles();
        Assert.NotEmpty(files);
    }

    [Fact]
    public async Task Search_NoFilter_ReturnsAllLines()
    {
        var result = await _service.SearchAsync();
        Assert.True(result.Lines.Count > 0);
    }

    [Fact]
    public async Task Search_ByLevel_FiltersCorrectly()
    {
        var result = await _service.SearchAsync(level: "ERR");
        Assert.All(result.Lines, l => Assert.Contains("ERR", l.Content));
    }

    [Fact]
    public async Task Search_ByQuery_FiltersCorrectly()
    {
        var result = await _service.SearchAsync(query: "Qdrant");
        Assert.Single(result.Lines);
        Assert.Contains("Qdrant", result.Lines[0].Content);
    }

    [Fact]
    public async Task Search_WithLimit_RespectsLimit()
    {
        var result = await _service.SearchAsync(limit: 2);
        Assert.True(result.Lines.Count <= 2);
    }

    [Fact]
    public async Task Search_CombinedQueryAndLevel()
    {
        var result = await _service.SearchAsync(query: "connect", level: "ERR");
        Assert.Single(result.Lines);
        Assert.Contains("LiteLLM", result.Lines[0].Content);
    }

    [Fact]
    public async Task Search_WithSince_TimeOnlyNotFiltered()
    {
        // Lines have time-only format [HH:mm:ss] which ExtractTimestamp returns null for
        var result = await _service.SearchAsync(since: "2026-04-28T00:00:00Z");
        Assert.True(result.Lines.Count >= 1);
    }

    [Fact]
    public void ListLogFiles_NoLogDirectory_ReturnsEmpty()
    {
        var tmpDir2 = Path.Combine(Path.GetTempPath(), $"LeanKernel-nologdir-{Guid.NewGuid():N}");
        var wikiDir2 = Path.Combine(tmpDir2, "wiki");
        Directory.CreateDirectory(wikiDir2);
        try
        {
            var config = Options.Create(new LeanKernelConfig { Wiki = new WikiConfig { BasePath = wikiDir2 } });
            var service = new LogReaderService(config);
            var files = service.ListLogFiles();
            Assert.Empty(files);
        }
        finally { try { Directory.Delete(tmpDir2, true); } catch { } }
    }

    [Fact]
    public async Task Search_SkipsBlankLines()
    {
        var logDir = Path.Combine(_tempDir, "logs");
        File.WriteAllText(Path.Combine(logDir, "LeanKernel-20260430.log"),
            "[12:00:00 INF] Valid line\n\n   \n[12:00:01 INF] Another line\n");

        var config = Options.Create(new LeanKernelConfig { Wiki = new WikiConfig { BasePath = Path.Combine(_tempDir, "wiki") } });
        var service = new LogReaderService(config);
        var result = await service.SearchAsync();

        // Blank lines should be skipped
        Assert.All(result.Lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Content)));
    }

    [Fact]
    public async Task Search_TotalFiles_ReportsCorrectCount()
    {
        var result = await _service.SearchAsync();
        Assert.Equal(1, result.TotalFiles);
    }

    [Fact]
    public async Task Search_LinesHaveFileAttribute()
    {
        var result = await _service.SearchAsync();
        Assert.All(result.Lines, l => Assert.NotEmpty(l.File));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
