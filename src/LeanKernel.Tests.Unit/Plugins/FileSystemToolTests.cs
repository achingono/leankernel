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
            var tool = new FileSystemTool(tmpDir);
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
            var tool = new FileSystemTool(tmpDir);
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
            var tool = new FileSystemTool(tmpDir);
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
            var tool = new FileSystemTool(tmpDir);
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
    public async Task ExecuteAsync_InvalidJson_ReturnsError()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            var tool = new FileSystemTool(tmpDir);
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
        var tool = new FileSystemTool("/tmp");
        Assert.Equal("file_read", tool.Name);
    }

    [Fact]
    public void Description_NotEmpty()
    {
        var tool = new FileSystemTool("/tmp");
        Assert.NotEmpty(tool.Description);
    }

    [Fact]
    public void ParametersSchema_ContainsPath()
    {
        var tool = new FileSystemTool("/tmp");
        Assert.Contains("path", tool.ParametersSchema);
    }
}
