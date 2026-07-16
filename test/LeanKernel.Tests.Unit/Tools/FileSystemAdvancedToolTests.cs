using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.BuiltIn.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class FileSystemAdvancedToolTests
{
    [Fact]
    public async Task Advanced_file_tools_cover_copy_move_edit_stat_search_touch_and_chmod()
    {
        var root = CreateTempRoot();
        var scopeFactory = CreateScopeFactory(root);

        await File.WriteAllTextAsync(Path.Combine(root, "docs", "source.txt"), "alpha beta");

        var stat = FileStatTool.Create(scopeFactory);
        var statFile = await stat.Handler!(new Dictionary<string, object?> { ["path"] = "docs/source.txt" }, CancellationToken.None);
        statFile.Success.Should().BeTrue();
        statFile.Output.Should().Contain("\"kind\":\"file\"");

        var statDir = await stat.Handler!(new Dictionary<string, object?> { ["path"] = "docs" }, CancellationToken.None);
        statDir.Success.Should().BeTrue();
        statDir.Output.Should().Contain("\"kind\":\"directory\"");

        var copy = FileCopyTool.Create(scopeFactory);
        var copyResult = await copy.Handler!(new Dictionary<string, object?> { ["sourcePath"] = "docs/source.txt", ["destinationPath"] = "docs/copy.txt" }, CancellationToken.None);
        copyResult.Success.Should().BeTrue();

        var move = FileMoveTool.Create(scopeFactory);
        var moveResult = await move.Handler!(new Dictionary<string, object?> { ["sourcePath"] = "docs/copy.txt", ["destinationPath"] = "docs/moved.txt" }, CancellationToken.None);
        moveResult.Success.Should().BeTrue();

        var edit = FileEditTool.Create(scopeFactory);
        var editResult = await edit.Handler!(new Dictionary<string, object?> { ["path"] = "docs/moved.txt", ["find"] = "beta", ["replace"] = "gamma" }, CancellationToken.None);
        editResult.Success.Should().BeTrue();

        var list = DirectoryListTool.Create(scopeFactory);
        var listResult = await list.Handler!(new Dictionary<string, object?> { ["path"] = "docs" }, CancellationToken.None);
        listResult.Success.Should().BeTrue();
        listResult.Output.Should().Contain("moved.txt");

        var touch = FileTouchTool.Create(scopeFactory);
        var touchResult = await touch.Handler!(new Dictionary<string, object?> { ["path"] = "docs/touched.txt" }, CancellationToken.None);
        touchResult.Success.Should().BeTrue();

        if (!OperatingSystem.IsWindows())
        {
            var chmod = FileChmodTool.Create(scopeFactory);
            var chmodResult = await chmod.Handler!(new Dictionary<string, object?> { ["path"] = "docs/moved.txt", ["mode"] = "644" }, CancellationToken.None);
            chmodResult.Success.Should().BeTrue();
        }
    }

    [Fact]
    public async Task FileCopy_rejects_destination_that_is_within_source_directory()
    {
        var root = CreateTempRoot();
        var scopeFactory = CreateScopeFactory(root);

        await File.WriteAllTextAsync(Path.Combine(root, "docs", "source.txt"), "alpha");

        var copy = FileCopyTool.Create(scopeFactory);
        var result = await copy.Handler!(new Dictionary<string, object?>
        {
            ["sourcePath"] = "docs",
            ["destinationPath"] = "docs/backup"
        }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("cannot be the source directory or a child");
    }

    [Fact]
    public async Task FileEdit_returns_validation_error_for_invalid_regex()
    {
        var root = CreateTempRoot();
        var scopeFactory = CreateScopeFactory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "docs", "regex.txt"), "alpha beta");

        var edit = FileEditTool.Create(scopeFactory);
        var result = await edit.Handler!(new Dictionary<string, object?>
        {
            ["path"] = "docs/regex.txt",
            ["find"] = "(",
            ["replace"] = "gamma",
            ["regex"] = true
        }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid regex pattern");
    }

    private static IServiceScopeFactory CreateScopeFactory(string root)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<FileSettings>(config =>
        {
            config.RootPath = root;
            config.ScratchRoot = Path.Combine(root, ".scratch");
            config.PythonExecutable = CreateFakePythonScript(root, "ignored");
        });

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "leankernel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "docs"));
        return path;
    }

    private static string CreateFakePythonScript(string root, string output)
    {
        var scriptPath = Path.Combine(root, "fake-python.sh");
        File.WriteAllText(scriptPath, $"#!/usr/bin/env bash\ncat <<'EOF'\n{output}\nEOF\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return scriptPath;
    }
}
