using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.BuiltIn;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class FileSearchToolTests
{
    private IServiceScopeFactory BuildScopeFactory(string rootPath)
    {
        var services = new ServiceCollection();
        services.Configure<FileSettings>(opts => opts.RootPath = rootPath);
        services.Configure<AgentSettings>(_ => { });
        var sp = services.BuildServiceProvider();

        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope())
            .Returns(() =>
            {
                var mockScope = new Mock<IServiceScope>();
                mockScope.Setup(s => s.ServiceProvider).Returns(sp);
                return mockScope.Object;
            });

        return mockFactory.Object;
    }

    [Fact]
    public async Task FileSearch_NoSearchTerms_ReturnsError()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpRoot);
        try
        {
            var tool = FileSearchTool.Create(BuildScopeFactory(tmpRoot));
            var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("required");
        }
        finally
        {
            Directory.Delete(tmpRoot, true);
        }
    }

    [Fact]
    public async Task FileSearch_EmptyRootPath_ReturnsError()
    {
        var tool = FileSearchTool.Create(BuildScopeFactory(string.Empty));
        var args = new Dictionary<string, object?> { ["query"] = "test" };

        var result = await tool.Handler(args, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("RootPath");
    }

    [Fact]
    public async Task FileSearch_TraversalPath_ReturnsAccessDenied()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpRoot);
        try
        {
            var tool = FileSearchTool.Create(BuildScopeFactory(tmpRoot));
            var args = new Dictionary<string, object?>
            {
                ["query"] = "test",
                ["path"] = "../../etc"
            };

            var result = await tool.Handler(args, CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Access denied");
        }
        finally
        {
            Directory.Delete(tmpRoot, true);
        }
    }

    [Fact]
    public async Task FileSearch_ByName_FindsMatchingFile()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpRoot);
        var testFile = Path.Combine(tmpRoot, "hello_world.txt");
        await File.WriteAllTextAsync(testFile, "some content");
        try
        {
            var tool = FileSearchTool.Create(BuildScopeFactory(tmpRoot));
            var args = new Dictionary<string, object?> { ["name"] = "hello_world" };

            var result = await tool.Handler(args, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Output.Should().Contain("hello_world");
        }
        finally
        {
            Directory.Delete(tmpRoot, true);
        }
    }

    [Fact]
    public async Task FileSearch_ByContent_FindsMatchingFile()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpRoot);
        var testFile = Path.Combine(tmpRoot, "notes.txt");
        await File.WriteAllTextAsync(testFile, "LeanKernel is awesome");
        try
        {
            var tool = FileSearchTool.Create(BuildScopeFactory(tmpRoot));
            var args = new Dictionary<string, object?> { ["content"] = "LeanKernel" };

            var result = await tool.Handler(args, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Output.Should().Contain("notes.txt");
        }
        finally
        {
            Directory.Delete(tmpRoot, true);
        }
    }

    [Fact]
    public async Task FileSearch_NonexistentDirectory_ReturnsError()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpRoot);
        try
        {
            var tool = FileSearchTool.Create(BuildScopeFactory(tmpRoot));
            var args = new Dictionary<string, object?>
            {
                ["name"] = "test",
                ["path"] = "nonexistent_dir"
            };

            var result = await tool.Handler(args, CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("not found");
        }
        finally
        {
            Directory.Delete(tmpRoot, true);
        }
    }

    [Fact]
    public void FileSearch_ToolName_IsCorrect()
    {
        var tool = FileSearchTool.Create(BuildScopeFactory(Path.GetTempPath()));
        tool.Name.Should().Be("file_search");
        tool.Category.Should().Be("filesystem");
    }

    [Fact]
    public async Task FileSearch_BothNameAndContentMatch_ReturnsNamePlusContentMatchType()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpRoot);
        var testFile = Path.Combine(tmpRoot, "kernel.txt");
        await File.WriteAllTextAsync(testFile, "LeanKernel is awesome");
        try
        {
            var tool = FileSearchTool.Create(BuildScopeFactory(tmpRoot));
            var args = new Dictionary<string, object?>
            {
                ["name"] = "kernel",
                ["content"] = "LeanKernel"
            };

            var result = await tool.Handler(args, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Output.Should().Contain("name\\u002Bcontent");
        }
        finally
        {
            Directory.Delete(tmpRoot, true);
        }
    }

    [Fact]
    public async Task FileSearch_ContentMatchNoNameMatch_ReturnsContentMatchType()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpRoot);
        var testFile = Path.Combine(tmpRoot, "readme.txt");
        await File.WriteAllTextAsync(testFile, "The quick brown fox");
        try
        {
            var tool = FileSearchTool.Create(BuildScopeFactory(tmpRoot));
            var args = new Dictionary<string, object?>
            {
                ["name"] = "nonexistent",
                ["content"] = "quick brown"
            };

            var result = await tool.Handler(args, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Output.Should().Contain("\"content\"");
        }
        finally
        {
            Directory.Delete(tmpRoot, true);
        }
    }

    [Fact]
    public async Task FileSearch_ContentNotInFile_NoMatch()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpRoot);
        await File.WriteAllTextAsync(Path.Combine(tmpRoot, "notes.txt"), "hello world");
        try
        {
            var tool = FileSearchTool.Create(BuildScopeFactory(tmpRoot));
            var args = new Dictionary<string, object?> { ["content"] = "NOTFOUND_XYZ" };

            var result = await tool.Handler(args, CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Output.Should().Contain("\"Matches\":[]");
        }
        finally
        {
            Directory.Delete(tmpRoot, true);
        }
    }
}