using LeanKernel.Plugins.BuiltIn;

namespace LeanKernel.Tests.Unit.Plugins;

public class FileSystemMutationToolTests
{
    [Fact]
    public async Task WriteTool_AllowsDefaultSelfMdPath()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            var tool = new FileSystemWriteTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"path": "SELF.md", "content": "hello"}""",
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(tmpDir, "SELF.md")));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task WriteTool_BlocksNonAllowlistedPathByDefault()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            var tool = new FileSystemWriteTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"path": "notes.md", "content": "hello"}""",
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("allowlist", result.Error!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task EditTool_UpdatesAllowlistedAgentsFile()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        var agentsDir = Path.Combine(tmpDir, "agents", "main");
        Directory.CreateDirectory(agentsDir);
        var agentsFile = Path.Combine(agentsDir, "AGENTS.md");
        await File.WriteAllTextAsync(agentsFile, "Tone: default");

        try
        {
            var tool = new FileSystemEditTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"path": "agents/main/AGENTS.md", "find": "default", "replace": "direct"}""",
                CancellationToken.None);

            Assert.True(result.Success);
            var content = await File.ReadAllTextAsync(agentsFile);
            Assert.Contains("direct", content);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task DeleteTool_RemovesAllowlistedUserFile()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var userPath = Path.Combine(tmpDir, "USER.md");
        await File.WriteAllTextAsync(userPath, "profile");

        try
        {
            var tool = new FileSystemDeleteTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"path": "USER.md"}""",
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.False(File.Exists(userPath));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}