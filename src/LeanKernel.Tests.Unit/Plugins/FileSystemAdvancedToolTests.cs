using LeanKernel.Plugins.BuiltIn;

namespace LeanKernel.Tests.Unit.Plugins;

public class FileSystemAdvancedToolTests
{
    private static async Task WithTempDirectoryAsync(Func<string, Task> test)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "LeanKernel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);

        try
        {
            await test(tmpDir);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public async Task CopyTool_CopiesFileToAllowlistedDestination()
    {
        await WithTempDirectoryAsync(async tmpDir =>
        {
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "source.txt"), "copy me");
            var tool = new FileSystemCopyTool(tmpDir);

            var result = await tool.ExecuteAsync(
                """{"sourcePath":"source.txt","destinationPath":"SELF.md"}""",
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("copy me", await File.ReadAllTextAsync(Path.Combine(tmpDir, "SELF.md")));
            Assert.Contains("Copied source.txt to SELF.md", result.Output);
        });
    }

    [Fact]
    public async Task CopyTool_CopiesDirectoryRecursively()
    {
        await WithTempDirectoryAsync(async tmpDir =>
        {
            Directory.CreateDirectory(Path.Combine(tmpDir, "source", "nested"));
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "source", "nested", "file.txt"), "nested");
            var tool = new FileSystemCopyTool(tmpDir, ["copies/"]);

            var result = await tool.ExecuteAsync(
                """{"sourcePath":"source","destinationPath":"copies/source-copy"}""",
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("nested", await File.ReadAllTextAsync(Path.Combine(tmpDir, "copies", "source-copy", "nested", "file.txt")));
        });
    }

    [Fact]
    public async Task CopyTool_RejectsDirectoryCopyWhenRecursiveFalse()
    {
        await WithTempDirectoryAsync(async tmpDir =>
        {
            Directory.CreateDirectory(Path.Combine(tmpDir, "source"));
            var tool = new FileSystemCopyTool(tmpDir, ["copies/"]);

            var result = await tool.ExecuteAsync(
                """{"sourcePath":"source","destinationPath":"copies/source-copy","recursive":false}""",
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("recursive=true", result.Error);
        });
    }

    [Fact]
    public async Task CopyTool_BlocksDestinationOutsideAllowlist()
    {
        await WithTempDirectoryAsync(async tmpDir =>
        {
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "source.txt"), "copy me");
            var tool = new FileSystemCopyTool(tmpDir);

            var result = await tool.ExecuteAsync(
                """{"sourcePath":"source.txt","destinationPath":"notes.md"}""",
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("allowlist", result.Error, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task CopyTool_ReturnsSourceNotFound()
    {
        await WithTempDirectoryAsync(async tmpDir =>
        {
            var tool = new FileSystemCopyTool(tmpDir);

            var result = await tool.ExecuteAsync(
                """{"sourcePath":"missing.txt","destinationPath":"SELF.md"}""",
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Source path not found", result.Error);
        });
    }

    [Fact]
    public async Task MoveTool_MovesFileToAllowlistedDestination()
    {
        await WithTempDirectoryAsync(async tmpDir =>
        {
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "USER.md"), "profile");
            var tool = new FileSystemMoveTool(tmpDir);

            var result = await tool.ExecuteAsync(
                """{"sourcePath":"USER.md","destinationPath":"SELF.md","overwrite":true}""",
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.False(File.Exists(Path.Combine(tmpDir, "USER.md")));
            Assert.Equal("profile", await File.ReadAllTextAsync(Path.Combine(tmpDir, "SELF.md")));
        });
    }

    [Fact]
    public async Task MoveTool_MovesDirectoryAndRejectsExistingDestinationWithoutOverwrite()
    {
        await WithTempDirectoryAsync(async tmpDir =>
        {
            Directory.CreateDirectory(Path.Combine(tmpDir, "agents", "main"));
            Directory.CreateDirectory(Path.Combine(tmpDir, "agents", "archive"));
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "agents", "main", "AGENTS.md"), "agent");
            var tool = new FileSystemMoveTool(tmpDir, ["agents/"]);

            var failed = await tool.ExecuteAsync(
                """{"sourcePath":"agents/main","destinationPath":"agents/archive"}""",
                CancellationToken.None);
            Assert.False(failed.Success);
            Assert.Contains("already exists", failed.Error);

            var moved = await tool.ExecuteAsync(
                """{"sourcePath":"agents/main","destinationPath":"agents/archive","overwrite":true}""",
                CancellationToken.None);
            Assert.True(moved.Success);
            Assert.True(File.Exists(Path.Combine(tmpDir, "agents", "archive", "AGENTS.md")));
        });
    }

    [Fact]
    public async Task MoveTool_ReturnsSourceNotFoundAndAccessDenied()
    {
        await WithTempDirectoryAsync(async tmpDir =>
        {
            var tool = new FileSystemMoveTool(tmpDir);

            var missing = await tool.ExecuteAsync(
                """{"sourcePath":"USER.md","destinationPath":"SELF.md"}""",
                CancellationToken.None);
            Assert.False(missing.Success);
            Assert.Contains("Source path not found", missing.Error);

            var denied = await tool.ExecuteAsync(
                """{"sourcePath":"../../USER.md","destinationPath":"SELF.md"}""",
                CancellationToken.None);
            Assert.False(denied.Success);
            Assert.Contains("outside the allowed directory", denied.Error);
        });
    }

    [Fact]
    public async Task ChmodTool_UpdatesModeForAllowlistedFile()
    {
        await WithTempDirectoryAsync(async tmpDir =>
        {
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "SELF.md"), "profile");
            var tool = new FileSystemChmodTool(tmpDir);

            var result = await tool.ExecuteAsync(
                """{"path":"SELF.md","mode":"0600"}""",
                CancellationToken.None);

            if (OperatingSystem.IsWindows())
            {
                Assert.False(result.Success);
                Assert.Contains("Unix-like", result.Error);
                return;
            }

            Assert.True(result.Success);
            Assert.Contains("Updated mode", result.Output);
            Assert.True((File.GetUnixFileMode(Path.Combine(tmpDir, "SELF.md")) & UnixFileMode.UserRead) != 0);
        });
    }

    [Fact]
    public async Task ChmodTool_ReturnsValidationErrors()
    {
        await WithTempDirectoryAsync(async tmpDir =>
        {
            var tool = new FileSystemChmodTool(tmpDir);

            var missing = await tool.ExecuteAsync(
                """{"path":"SELF.md","mode":"644"}""",
                CancellationToken.None);
            Assert.False(missing.Success);

            var denied = await tool.ExecuteAsync(
                """{"path":"notes.md","mode":"644"}""",
                CancellationToken.None);
            Assert.False(denied.Success);
            Assert.Contains("allowlist", denied.Error, StringComparison.OrdinalIgnoreCase);

            await File.WriteAllTextAsync(Path.Combine(tmpDir, "SELF.md"), "profile");
            var invalid = await tool.ExecuteAsync(
                """{"path":"SELF.md","mode":"bad"}""",
                CancellationToken.None);
            Assert.False(invalid.Success);
            Assert.Contains("Invalid mode", invalid.Error);
        });
    }
}
