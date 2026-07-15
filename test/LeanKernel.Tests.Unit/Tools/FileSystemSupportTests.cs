using FluentAssertions;
using LeanKernel.Logic.Tools.BuiltIn;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class FileSystemSupportTests
{
    [Fact]
    public void ResolveWithinRoot_ValidSubPath_ReturnsAbsolutePath()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpRoot);
        try
        {
            var result = FileSystemSupport.ResolveWithinRoot(tmpRoot, "sub/dir");

            result.Should().NotBeNull();
            result!.Should().StartWith(tmpRoot);
        }
        finally
        {
            Directory.Delete(tmpRoot, true);
        }
    }

    [Fact]
    public void ResolveWithinRoot_TraversalAttempt_ReturnsNull()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpRoot);
        try
        {
            var result = FileSystemSupport.ResolveWithinRoot(tmpRoot, "../../etc/passwd");

            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(tmpRoot, true);
        }
    }

    [Fact]
    public void ResolveWithinRoot_EmptySubPath_ReturnsRoot()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpRoot);
        try
        {
            var result = FileSystemSupport.ResolveWithinRoot(tmpRoot, null);

            result.Should().Be(Path.GetFullPath(tmpRoot));
        }
        finally
        {
            Directory.Delete(tmpRoot, true);
        }
    }

    [Fact]
    public void ResolveWithinRoot_EmptyRoot_ReturnsNull()
    {
        var result = FileSystemSupport.ResolveWithinRoot(string.Empty, "sub");
        result.Should().BeNull();
    }

    [Fact]
    public void IsWithinRoot_SubPath_ReturnsTrue()
    {
        var tmpRoot = Path.GetTempPath();
        var sub = Path.Combine(tmpRoot, "test", "file.txt");

        FileSystemSupport.IsWithinRoot(tmpRoot, sub).Should().BeTrue();
    }

    [Fact]
    public void IsWithinRoot_OutsidePath_ReturnsFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), "restricted");
        var outside = Path.Combine(Path.GetTempPath(), "other");

        FileSystemSupport.IsWithinRoot(root, outside).Should().BeFalse();
    }

    [Fact]
    public void IsWithinRoot_ExactRoot_ReturnsTrue()
    {
        var tmpRoot = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

        FileSystemSupport.IsWithinRoot(tmpRoot, tmpRoot).Should().BeTrue();
    }
}
