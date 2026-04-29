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

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
