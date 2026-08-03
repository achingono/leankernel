using FluentAssertions;

using LeanKernel.Logic.Tools.Dynamic;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class BinaryPathResolverTests
{
    [Fact]
    public void Resolve_FoundInPath_ReturnsAbsolutePath()
    {
        var binDir = CreateBinDir("lkbin-neo");
        try
        {
            var binaryPath = Path.Combine(binDir, "lk-tool");
            File.WriteAllText(binaryPath, "#!/bin/sh\necho hi\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(binaryPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }

            var resolved = BinaryPathResolver.Resolve("lk-tool", binDir);

            resolved.Should().Be(Path.GetFullPath(binaryPath));
        }
        finally
        {
            Directory.Delete(binDir, true);
        }
    }

    [Fact]
    public void Resolve_NotFound_ReturnsNull()
    {
        var binDir = CreateBinDir("prog-void");
        try
        {
            var resolved = BinaryPathResolver.Resolve("missing-tool-xyz", binDir);

            resolved.Should().BeNull();
        }
        finally
        {
            Directory.Delete(binDir, true);
        }
    }

    [Fact]
    public void Resolve_EmptyPathValue_ReturnsNull()
    {
        BinaryPathResolver.Resolve("any-tool", null).Should().BeNull();
        BinaryPathResolver.Resolve("any-tool", string.Empty).Should().BeNull();
    }

    [Fact]
    public void Resolve_BlankCommand_ReturnsNull()
    {
        BinaryPathResolver.Resolve("  ", "/usr/bin").Should().BeNull();
        BinaryPathResolver.Resolve(string.Empty, "/usr/bin").Should().BeNull();
    }

    [Fact]
    public void Resolve_AbsoluteExistingPath_ReturnsFullPath()
    {
        var file = Path.Combine(Path.GetTempPath(), $"lk-abs-{Guid.NewGuid():N}.tool");
        try
        {
            File.WriteAllText(file, "x");

            var resolved = BinaryPathResolver.Resolve(file, string.Empty);

            resolved.Should().Be(Path.GetFullPath(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Resolve_AbsoluteMissingPath_ReturnsNull()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"lk-missing-{Guid.NewGuid():N}.tool");
        BinaryPathResolver.Resolve(missing, "/usr/bin").Should().BeNull();
    }

    [Fact]
    public void Resolve_SearchesDirectoriesInOrder()
    {
        var first = CreateBinDir("prog-a");
        var second = CreateBinDir("prog-b");
        try
        {
            var name = "lk-first-win";
            File.WriteAllText(Path.Combine(first, name), "first");
            File.WriteAllText(Path.Combine(second, name), "second");

            var resolved = BinaryPathResolver.Resolve(name, string.Join(Path.PathSeparator, first, second));

            resolved.Should().Be(Path.GetFullPath(Path.Combine(first, name)));
        }
        finally
        {
            Directory.Delete(first, true);
            Directory.Delete(second, true);
        }
    }

    private static string CreateBinDir(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lk-bin-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}