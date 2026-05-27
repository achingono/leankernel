using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Tools.BuiltIn.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tests.Unit.Tools;

public class FileSystemToolsTests
{
    [Fact]
    public async Task FileReadTool_reads_plain_text_files()
    {
        var root = CreateTempRoot();
        var file = Path.Combine(root, "notes", "hello.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "hello world");

        var tool = FileReadTool.Create(CreateScopeFactory(root));
        var result = await tool.Handler!(new Dictionary<string, object?> { ["path"] = "notes/hello.txt" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("hello world");
    }

    [Fact]
    public async Task ExtractTextTool_uses_ocr_fallback_for_image_files()
    {
        var root = CreateTempRoot();
        var image = Path.Combine(root, "scan.png");
        await File.WriteAllBytesAsync(image, [1, 2, 3, 4]);
        var fakePython = CreateFakePythonScript(root, "ocr text");

        var tool = ExtractTextTool.Create(CreateScopeFactory(root, fakePython));
        var result = await tool.Handler!(new Dictionary<string, object?> { ["path"] = "scan.png" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("ocr text");
    }

    [Fact]
    public async Task FileWriteTool_writes_and_file_delete_tool_removes_files()
    {
        var root = CreateTempRoot();
        var scopeFactory = CreateScopeFactory(root);

        var write = FileWriteTool.Create(scopeFactory);
        var writeResult = await write.Handler!(new Dictionary<string, object?> { ["path"] = "docs/sample.txt", ["content"] = "sample text" }, CancellationToken.None);
        writeResult.Success.Should().BeTrue();

        var read = FileReadTool.Create(scopeFactory);
        var readResult = await read.Handler!(new Dictionary<string, object?> { ["path"] = "docs/sample.txt" }, CancellationToken.None);
        readResult.Success.Should().BeTrue();
        readResult.Output.Should().Be("sample text");

        var delete = FileDeleteTool.Create(scopeFactory);
        var deleteResult = await delete.Handler!(new Dictionary<string, object?> { ["path"] = "docs/sample.txt" }, CancellationToken.None);
        deleteResult.Success.Should().BeTrue();
        File.Exists(Path.Combine(root, "docs", "sample.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task DirectoryTools_create_and_list_entries()
    {
        var root = CreateTempRoot();
        var scopeFactory = CreateScopeFactory(root);

        var create = DirectoryCreateTool.Create(scopeFactory);
        var createResult = await create.Handler!(new Dictionary<string, object?> { ["path"] = "projects/demo" }, CancellationToken.None);
        createResult.Success.Should().BeTrue();

        await File.WriteAllTextAsync(Path.Combine(root, "projects", "demo", "alpha.txt"), "alpha");

        var list = DirectoryListTool.Create(scopeFactory);
        var listResult = await list.Handler!(new Dictionary<string, object?> { ["path"] = "projects/demo" }, CancellationToken.None);

        listResult.Success.Should().BeTrue();
        listResult.Output.Should().Contain("alpha.txt");
    }

    private static IServiceScopeFactory CreateScopeFactory(string root, string? pythonExecutable = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<LeanKernelConfig>(config =>
        {
            config.FileSystem.AllowedRoot = root;
            config.FileSystem.ScratchRoot = Path.Combine(root, ".scratch");
            if (!string.IsNullOrWhiteSpace(pythonExecutable))
            {
                config.FileSystem.PythonExecutable = pythonExecutable;
            }
        });

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "leankernel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateFakePythonScript(string root, string output)
    {
        var scriptPath = Path.Combine(root, "fake-python.sh");
        File.WriteAllText(scriptPath, $"#!/usr/bin/env bash\ncat >/dev/null\ncat <<'EOF'\n{output}\nEOF\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return scriptPath;
    }
}
