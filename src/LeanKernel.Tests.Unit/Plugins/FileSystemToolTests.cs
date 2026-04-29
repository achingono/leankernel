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
}
