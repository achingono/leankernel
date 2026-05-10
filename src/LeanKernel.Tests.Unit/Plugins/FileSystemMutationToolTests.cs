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
    public async Task WriteTool_AllowsEngagementFilesInAgentsMainPath()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            var tool = new FileSystemWriteTool(tmpDir);

            var selfResult = await tool.ExecuteAsync(
                """{"path": "agents/main/SELF.md", "content": "self"}""",
                CancellationToken.None);
            var userResult = await tool.ExecuteAsync(
                """{"path": "agents/main/USER.md", "content": "user"}""",
                CancellationToken.None);

            Assert.True(selfResult.Success);
            Assert.True(userResult.Success);
            Assert.True(File.Exists(Path.Combine(tmpDir, "agents", "main", "SELF.md")));
            Assert.True(File.Exists(Path.Combine(tmpDir, "agents", "main", "USER.md")));
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

    [Fact]
    public async Task TouchTool_CreatesAllowlistedAgentsFile()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            var tool = new FileSystemTouchTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"path": "agents/main/AGENTS.md"}""",
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.True(File.Exists(Path.Combine(tmpDir, "agents", "main", "AGENTS.md")));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task MkdirTool_AllowsParentDirectoryForAllowlistedAgentsFile()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            var tool = new DirectoryMkdirTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"path": "agents/main"}""",
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.True(Directory.Exists(Path.Combine(tmpDir, "agents", "main")));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task ListTool_ListsDirectoryEntries()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmpDir, "folder"));
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "file.txt"), "hello");

        try
        {
            var tool = new DirectoryListTool(tmpDir);
            var result = await tool.ExecuteAsync("""{}""", CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("file.txt", result.Output!);
            Assert.Contains("folder", result.Output!);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task StatTool_ReturnsMetadataForFile()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        await File.WriteAllTextAsync(Path.Combine(tmpDir, "SELF.md"), "hello");

        try
        {
            var tool = new FileSystemStatTool(tmpDir);
            var result = await tool.ExecuteAsync(
                """{"path": "SELF.md"}""",
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Contains("sizeBytes", result.Output!);
            Assert.Contains("SELF.md", result.Output!);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
