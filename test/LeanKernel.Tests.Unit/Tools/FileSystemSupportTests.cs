using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Tools.BuiltIn.Common;

namespace LeanKernel.Tests.Unit.Tools;

public class FileSystemSupportTests
{
    [Fact]
    public void NormalizeRelativePath_returns_empty_for_null()
    {
        FileSystemSupport.NormalizeRelativePath(null!).Should().BeEmpty();
    }

    [Fact]
    public void NormalizeRelativePath_returns_empty_for_whitespace()
    {
        FileSystemSupport.NormalizeRelativePath("   ").Should().BeEmpty();
    }

    [Fact]
    public void NormalizeRelativePath_strips_leading_slash()
    {
        FileSystemSupport.NormalizeRelativePath("/foo/bar").Should().Be("foo/bar");
    }

    [Fact]
    public void NormalizeRelativePath_converts_backslashes_to_forward_slashes()
    {
        FileSystemSupport.NormalizeRelativePath("foo\\bar\\baz").Should().Be("foo/bar/baz");
    }

    [Fact]
    public void NormalizeRelativePath_preserves_plain_relative_path()
    {
        FileSystemSupport.NormalizeRelativePath("foo/bar").Should().Be("foo/bar");
    }

    [Fact]
    public void ResolveWithinRoot_resolves_normal_relative_path()
    {
        var root = CreateTempRoot();
        try
        {
            var resolved = FileSystemSupport.ResolveWithinRoot(root, "sub/file.txt");
            resolved.Should().NotBeNull();
            resolved.Should().Be(Path.GetFullPath(Path.Combine(root, "sub/file.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveWithinRoot_strips_leading_slash_from_relative_path()
    {
        var root = CreateTempRoot();
        try
        {
            var resolved = FileSystemSupport.ResolveWithinRoot(root, "/sub/file.txt");
            resolved.Should().NotBeNull();
            resolved.Should().Be(Path.GetFullPath(Path.Combine(root, "sub/file.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveWithinRoot_returns_null_for_traversal_outside_root()
    {
        var root = CreateTempRoot();
        try
        {
            var resolved = FileSystemSupport.ResolveWithinRoot(root, "../outside.txt");
            resolved.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveWithinRoot_returns_null_for_deep_traversal_outside_root()
    {
        var root = CreateTempRoot();
        try
        {
            var resolved = FileSystemSupport.ResolveWithinRoot(root, "sub/../../outside.txt");
            resolved.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveWithinRoot_returns_root_path_for_empty_relative()
    {
        var root = CreateTempRoot();
        try
        {
            var resolved = FileSystemSupport.ResolveWithinRoot(root, "");
            resolved.Should().NotBeNull();
            resolved.Should().Be(Path.GetFullPath(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsWithinRoot_returns_true_for_child_path()
    {
        var root = Path.GetFullPath("/tmp");
        var child = Path.GetFullPath("/tmp/sub/file.txt");

        FileSystemSupport.IsWithinRoot(root, child).Should().BeTrue();
    }

    [Fact]
    public void IsWithinRoot_returns_true_for_root_itself()
    {
        var root = Path.GetFullPath("/tmp");

        FileSystemSupport.IsWithinRoot(root, root).Should().BeTrue();
    }

    [Fact]
    public void IsWithinRoot_returns_false_for_path_outside_root()
    {
        var root = Path.GetFullPath("/tmp/root");
        var outside = Path.GetFullPath("/tmp/other/file.txt");

        FileSystemSupport.IsWithinRoot(root, outside).Should().BeFalse();
    }

    [Fact]
    public void IsWithinRoot_is_case_insensitive_on_windows()
    {
        var root = Path.GetFullPath("/tmp/ROOT");
        var child = Path.GetFullPath("/tmp/root/Sub/File.txt");

        FileSystemSupport.IsWithinRoot(root, child).Should().BeTrue();
    }

    [Fact]
    public void IsTextLikeExtension_returns_true_for_known_text_extensions()
    {
        FileSystemSupport.IsTextLikeExtension("file.txt").Should().BeTrue();
        FileSystemSupport.IsTextLikeExtension("file.md").Should().BeTrue();
        FileSystemSupport.IsTextLikeExtension("file.json").Should().BeTrue();
        FileSystemSupport.IsTextLikeExtension("file.cs").Should().BeTrue();
        FileSystemSupport.IsTextLikeExtension("file.xml").Should().BeTrue();
        FileSystemSupport.IsTextLikeExtension("file.yaml").Should().BeTrue();
    }

    [Fact]
    public void IsTextLikeExtension_returns_false_for_binary_extensions()
    {
        FileSystemSupport.IsTextLikeExtension("file.png").Should().BeFalse();
        FileSystemSupport.IsTextLikeExtension("file.dll").Should().BeFalse();
        FileSystemSupport.IsTextLikeExtension("file.zip").Should().BeFalse();
    }

    [Fact]
    public void IsTextLikeExtension_returns_true_for_null_or_empty()
    {
        FileSystemSupport.IsTextLikeExtension(null).Should().BeTrue();
        FileSystemSupport.IsTextLikeExtension("").Should().BeTrue();
    }

    [Fact]
    public void IsOcrCandidate_returns_true_for_image_and_pdf_extensions()
    {
        FileSystemSupport.IsOcrCandidate("file.png").Should().BeTrue();
        FileSystemSupport.IsOcrCandidate("file.jpg").Should().BeTrue();
        FileSystemSupport.IsOcrCandidate("file.pdf").Should().BeTrue();
        FileSystemSupport.IsOcrCandidate("file.webp").Should().BeTrue();
        FileSystemSupport.IsOcrCandidate("file.tiff").Should().BeTrue();
    }

    [Fact]
    public void IsOcrCandidate_returns_false_for_non_image_extensions()
    {
        FileSystemSupport.IsOcrCandidate("file.txt").Should().BeFalse();
        FileSystemSupport.IsOcrCandidate("file.cs").Should().BeFalse();
    }

    [Fact]
    public void IsOcrCandidate_returns_false_for_null_or_empty()
    {
        FileSystemSupport.IsOcrCandidate(null).Should().BeFalse();
        FileSystemSupport.IsOcrCandidate("").Should().BeFalse();
    }

    [Fact]
    public void IsEpubCandidate_returns_true_for_epub()
    {
        FileSystemSupport.IsEpubCandidate("file.epub").Should().BeTrue();
    }

    [Fact]
    public void IsEpubCandidate_is_case_insensitive()
    {
        FileSystemSupport.IsEpubCandidate("file.EPUB").Should().BeTrue();
    }

    [Fact]
    public void IsEpubCandidate_returns_false_for_other_extensions()
    {
        FileSystemSupport.IsEpubCandidate("file.txt").Should().BeFalse();
    }

    [Fact]
    public void IsDocxCandidate_returns_true_for_docx()
    {
        FileSystemSupport.IsDocxCandidate("file.docx").Should().BeTrue();
    }

    [Fact]
    public void IsDocxCandidate_is_case_insensitive()
    {
        FileSystemSupport.IsDocxCandidate("file.DOCX").Should().BeTrue();
    }

    [Fact]
    public void IsDocxCandidate_returns_false_for_other_extensions()
    {
        FileSystemSupport.IsDocxCandidate("file.txt").Should().BeFalse();
    }

    [Fact]
    public void IsPptxCandidate_returns_true_for_pptx()
    {
        FileSystemSupport.IsPptxCandidate("file.pptx").Should().BeTrue();
    }

    [Fact]
    public void IsPptxCandidate_is_case_insensitive()
    {
        FileSystemSupport.IsPptxCandidate("file.PPTX").Should().BeTrue();
    }

    [Fact]
    public void IsPptxCandidate_returns_false_for_other_extensions()
    {
        FileSystemSupport.IsPptxCandidate("file.txt").Should().BeFalse();
    }

    [Fact]
    public void EnsureScratchPath_creates_directory_and_returns_path_with_extension()
    {
        var root = CreateTempRoot();
        try
        {
            var scratchRoot = Path.Combine(root, ".scratch");
            var config = new FileSystemConfig { ScratchRoot = scratchRoot };

            var path = FileSystemSupport.EnsureScratchPath(config, ".txt");

            Directory.Exists(scratchRoot).Should().BeTrue();
            path.Should().StartWith(scratchRoot);
            path.Should().EndWith(".txt");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveWithinRoot_returns_null_when_traversing_through_symlink()
    {
        var root = CreateTempRoot();
        try
        {
            var realDir = Directory.CreateDirectory(Path.Combine(root, "real"));
            var linkDir = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(linkDir, realDir.FullName);

            var resolved = FileSystemSupport.ResolveWithinRoot(root, "link/secret.txt");
            resolved.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveWithinRoot_returns_null_when_root_itself_is_symlink()
    {
        var container = CreateTempRoot();
        try
        {
            var realRoot = Directory.CreateDirectory(Path.Combine(container, "real-root"));
            var linkRoot = Path.Combine(container, "link-root");
            Directory.CreateSymbolicLink(linkRoot, realRoot.FullName);

            var resolved = FileSystemSupport.ResolveWithinRoot(linkRoot, "");

            resolved.Should().BeNull();
        }
        finally
        {
            Directory.Delete(container, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "leankernel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
